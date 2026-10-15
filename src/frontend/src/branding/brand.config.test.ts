import { describe, expect, it } from 'vitest'
import { brand, mobileNumberLabel, mobileNumberLabelFor } from './brand.config'

describe('client profile brand', () => {
  it('supplies every brand value the product renders', () => {
    for (const value of [brand.name, brand.shortName, brand.productWord, brand.organizationName]) {
      expect(value.trim()).not.toBe('')
    }
    expect(brand.logoOnLight).toBeTruthy()
    expect(brand.logoOnDark).toBeTruthy()
    expect(brand.logoAspectRatio).toBeGreaterThan(0)
  })

  it('labels the mobile number after the operator only when the profile names one', () => {
    expect(mobileNumberLabelFor('Exemplo')).toBe('Cartão Exemplo')
    expect(mobileNumberLabelFor(null)).toBe('N.º de telemóvel')
    expect(mobileNumberLabel).toBe(mobileNumberLabelFor(brand.mobileCarrierName))
  })
})
