import { brand } from '@client-profile/frontend/brand'

/**
 * The deployment's brand (Phase 9 scope — white-label readiness).
 * No component may hardcode a name, color, or logo — everything
 * client-specific flows through here and through the CSS custom properties
 * in src/app/theme.css, which the profile's own brand.css may override.
 *
 * The values come from the client profile selected at build time
 * (clients/<profile>/frontend/brand.ts, see docs/WHITE_LABEL.md).
 */
export { brand }

/** The mobile number's label: "Cartão <operator>" when the company names its operator (same rule as the backend's ResponsibilityTermText). */
export function mobileNumberLabelFor(mobileCarrierName: string | null) {
  return mobileCarrierName ? `Cartão ${mobileCarrierName}` : 'N.º de telemóvel'
}

export const mobileNumberLabel = mobileNumberLabelFor(brand.mobileCarrierName)
