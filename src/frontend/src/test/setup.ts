import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// jsdom does not implement layout APIs that components call (e.g. Combobox
// keeps the highlighted option in view); a no-op keeps behaviour testable.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {}
}

// Vitest runs without globals, so Testing Library's automatic cleanup is wired explicitly.
afterEach(() => {
  cleanup()
})
