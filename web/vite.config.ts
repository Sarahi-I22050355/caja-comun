import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Sin incrustar fuentes ni imágenes pequeñas como data: URI (la política de seguridad de contenido no las permite).
  build: { assetsInlineLimit: 0 },
  // En desarrollo las peticiones a /api van a la API que levanta docker compose.
  server: { proxy: { '/api': 'http://localhost:8080' } },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/pruebas/preparar.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
})
