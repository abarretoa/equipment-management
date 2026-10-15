import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import type { Plugin } from 'vite'

/**
 * Build-time client profile selection (docs/WHITE_LABEL.md), the frontend
 * twin of Directory.Build.props. One profile per build; never chosen at
 * runtime. First match wins:
 *   1. the CLIENT_PROFILE environment variable (Docker build argument);
 *   2. the one-line file clients/active-profile.txt, when it exists;
 *   3. "default", the neutral demonstration profile.
 * The profile directory is reachable from the application as `@client-profile`.
 */
export interface ResolvedClientProfile {
  id: string
  dir: string
  productName: string
}

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')

export function resolveClientProfile(): ResolvedClientProfile {
  const activeFile = path.join(repoRoot, 'clients', 'active-profile.txt')
  const id =
    process.env.CLIENT_PROFILE?.trim() ||
    (fs.existsSync(activeFile) ? fs.readFileSync(activeFile, 'utf8').trim() : '') ||
    'default'
  const dir = path.join(repoRoot, 'clients', id)
  const profileFile = path.join(dir, 'profile.json')
  if (!/^[a-z0-9][a-z0-9-]*$/.test(id) || !fs.existsSync(profileFile)) {
    throw new Error(
      `Client profile '${id}' not found: ${profileFile} does not exist. See docs/WHITE_LABEL.md.`,
    )
  }

  const profile = JSON.parse(fs.readFileSync(profileFile, 'utf8')) as { productName: string }
  return { id, dir, productName: profile.productName }
}

const escapeHtml = (value: string) =>
  value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

/** The profile's page title (index.html's %CLIENT_PRODUCT_NAME%) and its favicon, served and emitted as /favicon.svg. */
export function clientProfilePlugin(profile: ResolvedClientProfile): Plugin {
  const favicon = () => fs.readFileSync(path.join(profile.dir, 'assets', 'favicon.svg'))

  return {
    name: 'client-profile',
    transformIndexHtml: (html) =>
      html.replaceAll('%CLIENT_PRODUCT_NAME%', escapeHtml(profile.productName)),
    configureServer(server) {
      server.middlewares.use('/favicon.svg', (_req, res) => {
        res.setHeader('Content-Type', 'image/svg+xml')
        res.end(favicon())
      })
    },
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'favicon.svg', source: favicon() })
    },
  }
}
