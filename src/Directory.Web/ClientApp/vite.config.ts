import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': '/src',
    },
  },
  server: {
    port: 6173,
    strictPort: true,
    hmr: {
      // When the page is served through the ASP.NET Core proxy (https://localhost:6001),
      // the HMR client must connect directly to the Vite server, not the proxy.
      protocol: 'ws',
      host: 'localhost',
      port: 6173,
    },
    proxy: {
      '/api': {
        target: 'https://localhost:6001',
        changeOrigin: true,
        secure: false, // Accept the dev certificate
      },
    },
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
})
