import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConditionPhotoPicker } from '../../features/equipment/ConditionPhotoPicker'
import { PhotoGallery as HandoverPhotoGallery } from '../../features/handovers/PhotoGallery'
import { PhotoGallery as ReturnPhotoGallery } from '../../features/returns/PhotoGallery'
import { PHOTO_PRIVACY_HINT } from './PhotoPrivacyHint'

const noBlob = () => Promise.resolve(new Blob())
const file = (name: string) => new File(['x'], name, { type: 'image/jpeg' })

describe('photo privacy helper text', () => {
  // jsdom has no object URLs; the picker previews selected files with one.
  beforeEach(() => {
    vi.stubGlobal(
      'URL',
      Object.assign(URL, { createObjectURL: () => 'blob:photo', revokeObjectURL: () => {} }),
    )
  })
  afterEach(() => vi.unstubAllGlobals())

  it('reads as natural pt-PT guidance about what to photograph', () => {
    expect(PHOTO_PRIVACY_HINT).toBe(
      'Fotografe apenas o equipamento. Evite incluir pessoas, documentos, crachás ou ecrãs com informação pessoal.',
    )
  })

  it('accompanies the condition photo picker while photos can still be added', () => {
    const { rerender } = render(<ConditionPhotoPicker photos={[]} onChange={() => {}} />)
    expect(screen.getByText(PHOTO_PRIVACY_HINT)).toBeInTheDocument()

    rerender(
      <ConditionPhotoPicker
        photos={[file('a.jpg'), file('b.jpg'), file('c.jpg')]}
        onChange={() => {}}
      />,
    )
    expect(screen.queryByText(PHOTO_PRIVACY_HINT)).not.toBeInTheDocument()
  })

  it('accompanies the delivery inspection upload only while it is editable', () => {
    const { rerender } = render(
      <HandoverPhotoGallery
        photos={[]}
        getBlob={noBlob}
        onUpload={() => {}}
        hint="Carregar fotografia"
        editable
      />,
    )
    expect(screen.getByText(PHOTO_PRIVACY_HINT)).toBeInTheDocument()

    rerender(
      <HandoverPhotoGallery
        photos={[]}
        getBlob={noBlob}
        hint="Carregar fotografia"
        editable={false}
      />,
    )
    expect(screen.queryByText(PHOTO_PRIVACY_HINT)).not.toBeInTheDocument()
  })

  it('accompanies the return inspection and damage uploads only while they are editable', () => {
    const gallery = (editable: boolean) => (
      <QueryClientProvider client={new QueryClient()}>
        <ReturnPhotoGallery
          title="Fotografias da avaria"
          photos={[]}
          editable={editable}
          hint="Carregar fotografia"
          getBlob={noBlob}
          onUpload={() => Promise.resolve()}
          invalidateKey={['return', 'r-1']}
        />
      </QueryClientProvider>
    )
    const { rerender } = render(gallery(true))
    expect(screen.getByText(PHOTO_PRIVACY_HINT)).toBeInTheDocument()

    rerender(gallery(false))
    expect(screen.queryByText(PHOTO_PRIVACY_HINT)).not.toBeInTheDocument()
  })
})
