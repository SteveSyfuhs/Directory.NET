import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

// Routes that do not require authentication
const PUBLIC_ROUTES = new Set(['/login', '/password-reset', '/sspr-register'])

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/LoginView.vue'), meta: { public: true } },
    { path: '/forbidden', name: 'forbidden', component: () => import('../views/ForbiddenView.vue') },
    { path: '/', name: 'dashboard', component: () => import('../views/DashboardView.vue') },
    { path: '/browse', name: 'browse', component: () => import('../views/BrowseView.vue') },
    { path: '/search', name: 'search', component: () => import('../views/AdvancedSearchView.vue') },
    { path: '/users', name: 'users', component: () => import('../views/UserListView.vue'), meta: { permission: 'users:read' } },
    { path: '/groups', name: 'groups', component: () => import('../views/GroupListView.vue'), meta: { permission: 'groups:read' } },
    { path: '/computers', name: 'computers', component: () => import('../views/ComputerListView.vue'), meta: { permission: 'users:read' } },
    { path: '/ous', name: 'ous', component: () => import('../views/OuListView.vue'), meta: { permission: 'ous:manage' } },
    { path: '/recyclebin', name: 'recyclebin', component: () => import('../views/RecycleBinView.vue') },
    { path: '/audit', name: 'audit', component: () => import('../views/AuditLogView.vue'), meta: { permission: 'audit:read' } },
    { path: '/ldap-audit', name: 'ldap-audit', component: () => import('../views/LdapAuditView.vue'), meta: { permission: 'audit:read' } },
    { path: '/sites', name: 'sites', component: () => import('../views/SitesView.vue'), meta: { permission: 'sites:manage' } },
    { path: '/trusts', name: 'trusts', component: () => import('../views/TrustListView.vue'), meta: { permission: 'config:manage' } },
    { path: '/replication', name: 'replication', component: () => import('../views/ReplicationView.vue'), meta: { permission: 'config:manage' } },
    { path: '/schema', name: 'schema', component: () => import('../views/SchemaView.vue'), meta: { permission: 'schema:manage' } },
    { path: '/config', name: 'config', component: () => import('../views/DomainConfigView.vue'), meta: { permission: 'config:manage' } },
    { path: '/password-policies', name: 'password-policies', component: () => import('../views/PasswordPoliciesView.vue'), meta: { permission: 'config:manage' } },
    { path: '/account-lockout', name: 'account-lockout', component: () => import('../views/AccountLockoutView.vue'), meta: { permission: 'config:manage' } },
    { path: '/gpos', name: 'gpos', component: () => import('../views/GpoListView.vue'), meta: { permission: 'gpo:manage' } },
    { path: '/gpos/:id/edit', name: 'gpo-editor', component: () => import('../views/GpoEditorView.vue'), meta: { permission: 'gpo:manage' } },
    { path: '/service-accounts', name: 'service-accounts', component: () => import('../views/ServiceAccountsView.vue'), meta: { permission: 'users:read' } },
    { path: '/gmsa', name: 'gmsa', component: () => import('../views/GmsaView.vue'), meta: { permission: 'users:read' } },
    { path: '/certificates', name: 'certificates', component: () => import('../views/CertificatesView.vue'), meta: { permission: 'certificates:manage' } },
    { path: '/certificates/tls', name: 'tls-certificate', component: () => import('../views/TlsCertificateView.vue'), meta: { permission: 'certificates:manage' } },
    { path: '/dns', name: 'dns', component: () => import('../views/DnsManagerView.vue'), meta: { permission: 'dns:manage' } },
    { path: '/mfa', name: 'mfa', component: () => import('../views/MfaManagementView.vue'), meta: { permission: 'config:manage' } },
    { path: '/settings/cluster', name: 'cluster-settings', component: () => import('../views/ClusterSettingsView.vue'), meta: { permission: 'config:manage' } },
    { path: '/settings/nodes', name: 'node-settings', component: () => import('../views/NodeSettingsView.vue'), meta: { permission: 'config:manage' } },
    { path: '/settings/status', name: 'config-status', component: () => import('../views/ConfigStatusView.vue'), meta: { permission: 'config:manage' } },
    { path: '/service-settings', name: 'service-settings', component: () => import('../views/ServiceSettingsView.vue'), meta: { permission: 'config:manage' } },
    { path: '/backup', name: 'backup', component: () => import('../views/BackupView.vue'), meta: { permission: 'backup:manage' } },
    { path: '/ldif', name: 'ldif', component: () => import('../views/LdifView.vue'), meta: { permission: 'config:manage' } },
    { path: '/delegation', name: 'delegation', component: () => import('../views/DelegationView.vue'), meta: { permission: 'config:manage' } },
    { path: '/scheduled-tasks', name: 'scheduled-tasks', component: () => import('../views/ScheduledTasksView.vue'), meta: { permission: 'config:manage' } },
    { path: '/webhooks', name: 'webhooks', component: () => import('../views/WebhooksView.vue'), meta: { permission: 'config:manage' } },
    { path: '/sysvol', name: 'sysvol', component: () => import('../views/SysvolView.vue'), meta: { permission: 'config:manage' } },
    { path: '/rodc', name: 'rodc', component: () => import('../views/RodcView.vue'), meta: { permission: 'config:manage' } },
    { path: '/domain-join', name: 'domain-join', component: () => import('../views/DomainJoinView.vue'), meta: { permission: 'users:write' } },
    { path: '/computer-prestaging', name: 'computer-prestaging', component: () => import('../views/ComputerPrestagingView.vue'), meta: { permission: 'users:write' } },
    { path: '/join-verification', name: 'join-verification', component: () => import('../views/JoinVerificationView.vue'), meta: { permission: 'users:read' } },
    { path: '/dc-management', name: 'dc-management', component: () => import('../views/DcManagementView.vue'), meta: { permission: 'config:manage' } },
    { path: '/sspr-settings', name: 'sspr-settings', component: () => import('../views/SsprSettingsView.vue'), meta: { permission: 'config:manage' } },
    { path: '/sspr-register', name: 'sspr-register', component: () => import('../views/PasswordResetView.vue'), meta: { public: true } },
    { path: '/password-reset', name: 'password-reset', component: () => import('../views/PasswordResetView.vue'), meta: { public: true } },
    { path: '/oauth-clients', name: 'oauth-clients', component: () => import('../views/OAuthClientsView.vue'), meta: { permission: 'config:manage' } },
    { path: '/saml-providers', name: 'saml-providers', component: () => import('../views/SamlProvidersView.vue'), meta: { permission: 'config:manage' } },
    { path: '/scim-integrations', name: 'scim-integrations', component: () => import('../views/ScimIntegrationsView.vue'), meta: { permission: 'config:manage' } },
    { path: '/hr-sync', name: 'hr-sync', component: () => import('../views/HrSyncView.vue'), meta: { permission: 'config:manage' } },
    { path: '/workflows', name: 'workflows', component: () => import('../views/WorkflowsView.vue'), meta: { permission: 'config:manage' } },
    { path: '/fido2', name: 'fido2', component: () => import('../views/Fido2View.vue'), meta: { permission: 'config:manage' } },
    { path: '/conditional-access', name: 'conditional-access', component: () => import('../views/ConditionalAccessView.vue'), meta: { permission: 'config:manage' } },
    { path: '/smartcard', name: 'smartcard', component: () => import('../views/SmartCardView.vue'), meta: { permission: 'config:manage' } },
    { path: '/migration', name: 'migration', component: () => import('../views/MigrationView.vue'), meta: { permission: 'config:manage' } },
    { path: '/ldap-proxy', name: 'ldap-proxy', component: () => import('../views/LdapProxyView.vue'), meta: { permission: 'config:manage' } },
    { path: '/mdm', name: 'mdm', component: () => import('../views/MdmView.vue'), meta: { permission: 'config:manage' } },
    { path: '/multi-region', name: 'multi-region', component: () => import('../views/MultiRegionView.vue'), meta: { permission: 'config:manage' } },
    { path: '/pam', name: 'pam', component: () => import('../views/PamView.vue'), meta: { permission: 'config:manage' } },
    { path: '/radius', name: 'radius', component: () => import('../views/RadiusView.vue'), meta: { permission: 'config:manage' } },
    { path: '/ssh-keys', name: 'ssh-keys', component: () => import('../views/SshKeysView.vue'), meta: { permission: 'config:manage' } },
    { path: '/compliance', name: 'compliance', component: () => import('../views/ComplianceView.vue'), meta: { permission: 'compliance:manage' } },
    { path: '/access-reviews', name: 'access-reviews', component: () => import('../views/AccessReviewsView.vue'), meta: { permission: 'compliance:manage' } },
    { path: '/data-retention', name: 'data-retention', component: () => import('../views/DataRetentionView.vue'), meta: { permission: 'compliance:manage' } },
  ],
})

// Global navigation guard — redirect to /login when not authenticated, /forbidden when lacking permission
router.beforeEach(async (to) => {
  // Always allow public routes
  if (to.meta.public || PUBLIC_ROUTES.has(to.path)) {
    return true
  }

  const authStore = useAuthStore()

  // Check session once per page load (checkAuth is a no-op if already checked)
  await authStore.checkAuth()

  if (!authStore.isAuthenticated) {
    return { path: '/login', query: { returnUrl: to.fullPath } }
  }

  // Permission-based route guard
  const requiredPermission = to.meta.permission as string | undefined
  if (requiredPermission && !authStore.hasPermission(requiredPermission)) {
    return { path: '/forbidden' }
  }

  return true
})

export default router
