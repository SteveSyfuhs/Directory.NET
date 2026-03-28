import { ref, onUnmounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { getMe } from '../api/auth'
import { useAuthStore } from '../stores/auth'

/** How often we poll /auth/me to validate the session (ms). */
const POLL_INTERVAL = 5 * 60 * 1000 // 5 minutes

/** How long before the next poll we show the "session expiring" warning (ms). */
const WARNING_LEAD_TIME = 5 * 60 * 1000 // 5 minutes

/**
 * Composable that monitors the user's session.
 *
 * - Polls `/api/v1/auth/me` every 5 minutes while the user is authenticated.
 * - Tracks time since the last successful auth check.
 * - When the session is likely close to expiry (approaching the next poll with
 *   no recent activity), shows a PrimeVue confirmation dialog offering to extend.
 * - If the session has already expired (401), clears auth and redirects to login.
 */
export function useSessionMonitor() {
  const auth = useAuthStore()
  const router = useRouter()
  const toast = useToast()
  const confirm = useConfirm()

  let pollTimer: ReturnType<typeof setInterval> | null = null
  let warningTimer: ReturnType<typeof setTimeout> | null = null
  const lastSuccessfulCheck = ref<number>(Date.now())

  /** Call /auth/me to validate (and refresh) the session cookie. */
  async function checkSession(): Promise<boolean> {
    try {
      const user = await getMe()
      if (user) {
        lastSuccessfulCheck.value = Date.now()
        scheduleWarning()
        return true
      }
      // null means 401 – session gone
      handleExpired()
      return false
    } catch {
      // Network error or unexpected failure – don't immediately log out,
      // let the next poll or a 401 interceptor handle it.
      return false
    }
  }

  /** Redirect to login when session has expired. */
  function handleExpired() {
    stopMonitoring()
    if (!auth.isAuthenticated) return
    auth.clearSession()

    const currentPath = router.currentRoute.value.fullPath
    if (!currentPath.startsWith('/login')) {
      toast.add({
        severity: 'warn',
        summary: 'Session Expired',
        detail: 'Your session has expired. Please log in again.',
        life: 5000,
      })
      router.push({ path: '/login', query: { returnUrl: currentPath, expired: '1' } })
    }
  }

  /** Show a warning dialog ~5 minutes before the session would go stale. */
  function scheduleWarning() {
    if (warningTimer) clearTimeout(warningTimer)

    // We show the warning WARNING_LEAD_TIME before the next poll fires.
    // Since the poll itself is POLL_INTERVAL, the warning fires at
    // POLL_INTERVAL - WARNING_LEAD_TIME after the last successful check.
    // If WARNING_LEAD_TIME >= POLL_INTERVAL, warn immediately before next poll.
    const delay = Math.max(POLL_INTERVAL - WARNING_LEAD_TIME, 0)

    warningTimer = setTimeout(() => {
      if (!auth.isAuthenticated) return

      confirm.require({
        header: 'Session Expiring Soon',
        message: 'Your session will expire in 5 minutes. Click to extend your session.',
        icon: 'pi pi-clock',
        acceptLabel: 'Extend Session',
        rejectLabel: 'Dismiss',
        accept: async () => {
          const ok = await checkSession()
          if (ok) {
            toast.add({
              severity: 'success',
              summary: 'Session Extended',
              detail: 'Your session has been refreshed.',
              life: 3000,
            })
          }
        },
      })
    }, delay)
  }

  function startMonitoring() {
    // Initial timestamp
    lastSuccessfulCheck.value = Date.now()
    scheduleWarning()

    // Poll on interval
    pollTimer = setInterval(async () => {
      if (!auth.isAuthenticated) {
        stopMonitoring()
        return
      }
      await checkSession()
    }, POLL_INTERVAL)
  }

  function stopMonitoring() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
    if (warningTimer) {
      clearTimeout(warningTimer)
      warningTimer = null
    }
  }

  // Start/stop based on auth state
  watch(
    () => auth.isAuthenticated,
    (authed) => {
      if (authed) {
        startMonitoring()
      } else {
        stopMonitoring()
      }
    },
    { immediate: true },
  )

  onUnmounted(() => {
    stopMonitoring()
  })

  return { lastSuccessfulCheck, checkSession }
}
