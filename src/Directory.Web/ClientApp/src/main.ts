import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import Lara from '@primeuix/themes/lara'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'
import 'primeicons/primeicons.css'

import App from './App.vue'
import router from './router'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(router)
app.use(PrimeVue, {
  theme: {
    preset: Lara,
    options: {
      darkModeSelector: '.dark-mode',
    },
  },
})

import { useThemeStore } from './stores/theme'
useThemeStore()

app.use(ToastService)
app.use(ConfirmationService)
app.directive('tooltip', Tooltip)

import { vPermission } from './directives/permission'
app.directive('permission', vPermission)

app.mount('#app')
