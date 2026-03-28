import type { Directive, DirectiveBinding } from 'vue'
import { useAuthStore } from '../stores/auth'

function evaluate(el: HTMLElement, binding: DirectiveBinding) {
  const authStore = useAuthStore()
  const value = binding.value

  let visible = false
  if (typeof value === 'string') {
    visible = authStore.hasPermission(value)
  } else if (Array.isArray(value)) {
    visible = authStore.hasAnyPermission(value)
  }

  if (visible) {
    el.style.removeProperty('display')
  } else {
    el.style.display = 'none'
  }
}

export const vPermission: Directive = {
  mounted(el: HTMLElement, binding: DirectiveBinding) {
    evaluate(el, binding)
  },
  updated(el: HTMLElement, binding: DirectiveBinding) {
    evaluate(el, binding)
  },
}
