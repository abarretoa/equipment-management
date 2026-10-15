import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'
import { resolveClientProfile } from './clientProfile.ts'

// Component tests only (jsdom). Kept separate from vite.config.ts so the dev
// server/proxy configuration is untouched; Tailwind is not needed to test
// behaviour, so CSS is not processed here. Tests run against the same
// client profile a build would select, and read brand values from it rather
// than hardcoding any client's name.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@client-profile': resolveClientProfile().dir },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
    restoreMocks: true,
    unstubGlobals: true,
  },
})
