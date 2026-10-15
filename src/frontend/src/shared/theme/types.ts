/**
 * Shape of the centralized brand configuration (white-label
 * requirement, Phase 9 scope). A new
 * client deployment supplies its own BrandConfig — components never hardcode
 * a client's name, colors, or copy; they read from here instead.
 */
export interface BrandConfig {
  /** Full product name — page title, accessible name of the product lockup. */
  name: string
  /** Short form for compact spaces (the wordmark's accessible text). */
  shortName: string
  /** The product word set beside the wordmark in the lockup (e.g. "Equipment"). */
  productWord: string
  /** The company running this deployment, as written in copy ("funcionários internos da …"). */
  organizationName: string
  /** The mobile operator named on the company's documents, if any (labels the mobile number "Cartão <operator>"). */
  mobileCarrierName: string | null
  /** Support contact shown in error states / footer, if any. */
  supportEmail?: string
  /** Wordmark for light surfaces (official source asset). */
  logoOnLight: string
  /** Wordmark for the dark ink surfaces (derived reversed variant of the same asset). */
  logoOnDark: string
  /** Width / height of the wordmark assets, used to keep the aspect ratio exact. */
  logoAspectRatio: number
}
