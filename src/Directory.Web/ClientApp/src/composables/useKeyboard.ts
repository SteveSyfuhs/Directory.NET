import { onMounted, onUnmounted } from 'vue'

export interface KeyboardShortcut {
  /** The key value (e.g. 'k', '/', 'Escape') */
  key: string
  /** Require Ctrl (or Cmd on Mac) to be held */
  ctrl?: boolean
  /** Require Alt to be held */
  alt?: boolean
  /** Require Shift to be held */
  shift?: boolean
  /** Handler to call when the shortcut fires */
  handler: (event: KeyboardEvent) => void
}

/**
 * Register global keyboard shortcuts for the lifetime of the calling component.
 * Shortcuts are automatically removed when the component is unmounted.
 */
export function useKeyboard(shortcuts: KeyboardShortcut[]) {
  function onKeydown(event: KeyboardEvent) {
    for (const shortcut of shortcuts) {
      const keyMatch = event.key === shortcut.key
      const ctrlMatch = shortcut.ctrl
        ? (event.ctrlKey || event.metaKey)
        : !event.ctrlKey && !event.metaKey
      const altMatch = shortcut.alt ? event.altKey : !event.altKey
      const shiftMatch = shortcut.shift ? event.shiftKey : true // shift is optional by default

      if (keyMatch && ctrlMatch && altMatch && shiftMatch) {
        shortcut.handler(event)
      }
    }
  }

  onMounted(() => {
    document.addEventListener('keydown', onKeydown)
  })

  onUnmounted(() => {
    document.removeEventListener('keydown', onKeydown)
  })
}
