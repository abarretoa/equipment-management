import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Self-hosted Geist / Geist Mono (SIL OFL 1.1, via Fontsource): bundled by Vite
// with font-display: swap — no runtime request to any third-party font host.
import '@fontsource-variable/geist'
import '@fontsource-variable/geist-mono'
import './index.css'
// The client profile's brand tokens (docs/WHITE_LABEL.md), after the product theme so they win.
import '@client-profile/frontend/brand.css'
import App from './app/App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
