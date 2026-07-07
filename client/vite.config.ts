/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { configDefaults } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/tests/setup.ts'],
    // e2e/ holds Playwright specs, which run under `playwright test` (npm run test:e2e),
    // not Vitest — they use @playwright/test's own runner and browser fixtures.
    exclude: [...configDefaults.exclude, 'e2e/**'],
  },
})
