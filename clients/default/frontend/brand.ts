import type { BrandConfig } from '../../../src/frontend/src/shared/theme/types'
import profile from '../profile.json'
import logo from '../assets/logo.svg'
import logoReversed from '../assets/logo-reversed.svg'

/** Neutral demonstration brand: a fictional organisation ("Acme Consulting"). */
export const brand: BrandConfig = {
  name: profile.productName,
  shortName: profile.shortName,
  productWord: profile.productWord,
  organizationName: profile.organizationName,
  mobileCarrierName: profile.mobileCarrierName,
  logoOnLight: logo,
  logoOnDark: logoReversed,
  logoAspectRatio: 114 / 32,
}
