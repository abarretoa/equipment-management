import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { endOfLocalDayAsUtc, startOfLocalDayAsUtc } from './auditDateRange'

describe('audit date range (Europe/Lisbon)', () => {
  // Node re-reads process.env.TZ at runtime, so this pins the local timezone.
  beforeEach(() => {
    vi.stubEnv('TZ', 'Europe/Lisbon')
  })
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('summer time (UTC+1): a day spans 23:00Z of the previous day to 22:59:59.999Z', () => {
    expect(startOfLocalDayAsUtc('2026-10-10')).toBe('2026-10-09T23:00:00.000Z')
    expect(endOfLocalDayAsUtc('2026-10-10')).toBe('2026-10-10T22:59:59.999Z')
  })

  it('winter time (UTC+0) and the DST change day are both whole local days', () => {
    expect(startOfLocalDayAsUtc('2026-12-10')).toBe('2026-12-10T00:00:00.000Z')
    expect(endOfLocalDayAsUtc('2026-12-10')).toBe('2026-12-10T23:59:59.999Z')
    // 25 Oct 2026: clocks go back at 02:00 WEST → the local day lasts 25 hours.
    expect(startOfLocalDayAsUtc('2026-10-25')).toBe('2026-10-24T23:00:00.000Z')
    expect(endOfLocalDayAsUtc('2026-10-25')).toBe('2026-10-25T23:59:59.999Z')
  })

  it('an empty or partial date sends no bound at all', () => {
    expect(startOfLocalDayAsUtc('')).toBeUndefined()
    expect(endOfLocalDayAsUtc('2026-10')).toBeUndefined()
  })
})
