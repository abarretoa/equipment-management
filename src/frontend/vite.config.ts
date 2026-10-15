import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig, loadEnv, searchForWorkspaceRoot } from 'vite'
import { clientProfilePlugin, resolveClientProfile } from './clientProfile.ts'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // VITE_PROXY_TARGET lets an alternate stack (e.g. a Demo build running its
  // own API on a different port so it can run side by side with the normal
  // dev API) point this same proxy at itself, without touching the normal
  // dev default below.
  const env = loadEnv(mode, process.cwd(), 'VITE_')
  const proxyTarget = env.VITE_PROXY_TARGET || 'https://localhost:7047'
  const clientProfile = resolveClientProfile()

  return {
    plugins: [react(), tailwindcss(), clientProfilePlugin(clientProfile)],
    resolve: {
      alias: { '@client-profile': clientProfile.dir },
    },
    server: {
      // The client profile lives outside this package (clients/<profile>/).
      fs: { allow: [searchForWorkspaceRoot(process.cwd()), clientProfile.dir] },
      // Proxies API calls through the same origin the frontend is served
      // from, so the browser never sees a cross-scheme http(frontend)->
      // https(backend) request. That matters because the backend's
      // auth/CSRF cookies are Secure + SameSite=Strict (ADR-003/004): under
      // "schemeful same-site" enforcement, http and https on the same host
      // count as different sites, which silently drops those cookies and
      // breaks login. Matches the Phase 0 seam already documented in
      // .env.example ("behind a dev-server proxy configured later") — leave
      // VITE_API_BASE_URL unset locally.
      proxy: {
        '/api': {
          target: proxyTarget,
          changeOrigin: true,
          secure: false,
        },
      },
    },
  }
})
