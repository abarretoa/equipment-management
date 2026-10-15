import { brand } from './brand.config'
import { cn } from '../shared/utils/cn'

/**
 * Lockup construction (Visual System v1, "ProductLockup"): the client's
 * wordmark at height H, a brand marker (2px wide below H 20, 3px from H 20,
 * ≈0.9 H tall), then the product word in Geist 500. The marker is the only
 * brand-colour element on screen besides the active-navigation marker.
 */
const SIZES = {
  /** Navigation rail and drawer (minimum H). */
  sm: { logo: 14, gap: 9, markerWidth: 2, markerHeight: 13, word: 14 },
  /** Compact dark header. */
  md: { logo: 16, gap: 9, markerWidth: 2, markerHeight: 14, word: 15 },
  /** Login brand panel. */
  lg: { logo: 26, gap: 14, markerWidth: 3, markerHeight: 23, word: 24 },
} as const

interface LogoProps {
  size?: keyof typeof SIZES
  /** The surface the lockup sits on: `dark` uses the reversed wordmark and white product word. */
  tone?: 'light' | 'dark'
  /** Wordmark only, without the marker and product word. */
  collapsed?: boolean
  className?: string
}

/** Product lockup — every caller uses <Logo />; the brand itself comes from brand.config.ts. */
export function Logo({ size = 'sm', tone = 'light', collapsed = false, className }: LogoProps) {
  const s = SIZES[size]

  return (
    <span className={cn('inline-flex shrink-0 items-center', className)} style={{ gap: s.gap }}>
      <img
        src={tone === 'dark' ? brand.logoOnDark : brand.logoOnLight}
        alt={brand.shortName}
        height={s.logo}
        width={Math.round(s.logo * brand.logoAspectRatio)}
        style={{ height: s.logo, width: 'auto' }}
        className="block"
      />
      {!collapsed && (
        <>
          <span
            aria-hidden
            className="block bg-brand-marker"
            style={{ width: s.markerWidth, height: s.markerHeight }}
          />
          <span
            className={cn(
              'leading-none font-medium tracking-[-0.01em]',
              tone === 'dark' ? 'text-rail-fg-active' : 'text-fg',
            )}
            style={{ fontSize: s.word }}
          >
            {brand.productWord}
          </span>
        </>
      )}
    </span>
  )
}
