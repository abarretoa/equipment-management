import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'

/**
 * One object URL per loaded Blob, shared by every component showing it (a
 * thumbnail and the PhotoViewer) and reference-counted. Revocation waits a
 * tick, so an effect re-run (StrictMode mount→cleanup→mount, or the viewer
 * mounting while a thumbnail holds the same blob) re-acquires the live URL
 * instead of rendering a revoked one.
 */
const objectUrls = new Map<Blob, { url: string; refs: number }>()

function objectUrlFor(blob: Blob): string {
  let entry = objectUrls.get(blob)
  if (!entry) {
    entry = { url: URL.createObjectURL(blob), refs: 0 }
    objectUrls.set(blob, entry)
  }
  return entry.url
}

/**
 * Loads a private image through its authenticated `.../content` endpoint
 * (`apiFetchBlob` — cookie session, never a public storage URL) and exposes it
 * as a short-lived object URL. Thumbnails and the PhotoViewer pass the same
 * `queryKey`, so opening the viewer reuses the blob the thumbnail already
 * fetched instead of downloading it again.
 */
export function usePrivateImageUrl(queryKey: readonly unknown[], load: () => Promise<Blob>) {
  const query = useQuery({ queryKey, queryFn: load, staleTime: Infinity })
  const blob = query.data
  const url = blob ? objectUrlFor(blob) : null

  useEffect(() => {
    if (!blob) return
    objectUrlFor(blob)
    const entry = objectUrls.get(blob)!
    entry.refs += 1
    return () => {
      entry.refs -= 1
      setTimeout(() => {
        if (entry.refs === 0 && objectUrls.get(blob) === entry) {
          objectUrls.delete(blob)
          URL.revokeObjectURL(entry.url)
        }
      }, 0)
    }
  }, [blob])

  return { url, failed: query.isError }
}
