/**
 * The selected client profile's frontend module (clients/<profile>/frontend/,
 * aliased as `@client-profile` by vite.config.ts and vitest.config.ts — see
 * docs/WHITE_LABEL.md). Every profile's brand.ts is type-checked against
 * BrandConfig by tsconfig.app.json.
 */
declare module '@client-profile/frontend/brand' {
  export const brand: import('./shared/theme/types').BrandConfig
}
