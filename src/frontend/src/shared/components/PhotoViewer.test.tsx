import { StrictMode, useState } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PhotoThumbnailButton, PhotoViewer, type ViewerPhoto } from './PhotoViewer'
import { usePrivateImageUrl } from '../hooks/usePrivateImageUrl'

const photos: ViewerPhoto[] = [
  { id: 'a', alt: 'Fotografia 1 de 3 (frente.jpg)', fileName: 'frente.jpg' },
  { id: 'b', alt: 'Fotografia 2 de 3 (verso.jpg)', fileName: 'verso.jpg' },
  { id: 'c', alt: 'Fotografia 3 de 3 (lateral.jpg)', fileName: 'lateral.jpg' },
]

let urlCounter = 0
beforeEach(() => {
  urlCounter = 0
  vi.stubGlobal(
    'URL',
    Object.assign(URL, {
      createObjectURL: () => `blob:photo-${++urlCounter}`,
      revokeObjectURL: () => {},
    }),
  )
})
afterEach(() => vi.unstubAllGlobals())

/** A minimal gallery: one thumbnail button per photo, and the shared viewer. */
function Gallery({
  items,
  getBlob,
}: {
  items: ViewerPhoto[]
  getBlob: (id: string) => Promise<Blob>
}) {
  const [index, setIndex] = useState<number | null>(null)
  return (
    <>
      {items.map((photo, i) => (
        <PhotoThumbnailButton
          key={photo.id}
          label={`Ampliar ${photo.alt}`}
          onOpen={() => setIndex(i)}
        >
          <span>{photo.fileName}</span>
        </PhotoThumbnailButton>
      ))}
      <PhotoViewer
        title="Fotografias atuais"
        photos={items}
        index={index}
        onIndexChange={setIndex}
        blobKey={(id) => ['test-blob', id]}
        getBlob={getBlob}
      />
    </>
  )
}

function renderGallery(items = photos) {
  const getBlob = vi.fn((id: string) => Promise.resolve(new Blob([id], { type: 'image/jpeg' })))
  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <Gallery items={items} getBlob={getBlob} />
    </QueryClientProvider>,
  )
  return { getBlob }
}

const viewer = () => screen.getByRole('dialog', { name: 'Fotografias atuais' })
const zoomOf = async (alt: string) => (await screen.findByRole('img', { name: alt })).style.width

describe('PhotoViewer', () => {
  it('opens the clicked photo enlarged with its counter', async () => {
    renderGallery()
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 2 de 3 (verso.jpg)' }),
    )

    expect(viewer()).toBeInTheDocument()
    expect(await screen.findByRole('img', { name: 'Fotografia 2 de 3 (verso.jpg)' })).toHaveClass(
      'object-contain',
    )
    expect(screen.getByText('2 de 3')).toBeInTheDocument()
  })

  it('shows the gallery title in the header, never the stored file name', async () => {
    const generated = 'f131a42f9fade112c2fa10489e3307d9.jpg'
    renderGallery([
      { id: 'h1', alt: 'Fotografia 1 de 2', fileName: generated },
      { id: 'h2', alt: 'Fotografia 2 de 2', fileName: 'outra.jpg' },
    ])
    await userEvent.click(screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 2' }))
    await screen.findByRole('img', { name: 'Fotografia 1 de 2' })

    const dialog = viewer()
    expect(within(dialog).getByRole('heading', { name: 'Fotografias atuais' })).toBeInTheDocument()
    expect(dialog).not.toHaveTextContent(generated)
    expect(dialog.querySelector(`[title="${generated}"]`)).toBeNull()
  })

  it('gives a multi-photo gallery its own visible navigation row on mobile', async () => {
    renderGallery()
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 3 (frente.jpg)' }),
    )

    const previous = screen.getByRole('button', { name: 'Fotografia anterior' })
    const next = screen.getByRole('button', { name: 'Fotografia seguinte' })
    // Always rendered (no hover reveal), 44px below lg, with a filled surface.
    for (const button of [previous, next]) {
      expect(button).toBeVisible()
      expect(button).toHaveClass('size-11', 'max-lg:bg-rail-active', 'max-lg:border')
      expect(button.className).not.toMatch(/opacity-0|group-hover/)
    }
    // Previous · counter · next share one full-width row, separate from zoom.
    const navRow = previous.parentElement!
    expect(navRow).toBe(next.parentElement)
    expect(navRow).toContainElement(screen.getByText('1 de 3'))
    expect(navRow).toHaveClass('max-lg:w-full', 'max-lg:justify-between')
    const zoomGroup = screen.getByRole('group', { name: 'Zoom' })
    expect(navRow).not.toContainElement(zoomGroup)
    expect(zoomGroup).toHaveClass('max-lg:w-full')
  })

  it('moves between photos with the previous/next buttons, wrapping at the ends', async () => {
    renderGallery()
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 3 (frente.jpg)' }),
    )

    await userEvent.click(screen.getByRole('button', { name: 'Fotografia seguinte' }))
    expect(
      await screen.findByRole('img', { name: 'Fotografia 2 de 3 (verso.jpg)' }),
    ).toBeInTheDocument()
    expect(screen.getByText('2 de 3')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Fotografia anterior' }))
    await userEvent.click(screen.getByRole('button', { name: 'Fotografia anterior' }))
    expect(
      await screen.findByRole('img', { name: 'Fotografia 3 de 3 (lateral.jpg)' }),
    ).toBeInTheDocument()
    expect(screen.getByText('3 de 3')).toBeInTheDocument()
  })

  it('moves between photos with ArrowRight / ArrowLeft', async () => {
    renderGallery()
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 3 (frente.jpg)' }),
    )

    await userEvent.keyboard('{ArrowRight}')
    expect(
      await screen.findByRole('img', { name: 'Fotografia 2 de 3 (verso.jpg)' }),
    ).toBeInTheDocument()
    await userEvent.keyboard('{ArrowRight}')
    expect(
      await screen.findByRole('img', { name: 'Fotografia 3 de 3 (lateral.jpg)' }),
    ).toBeInTheDocument()
    await userEvent.keyboard('{ArrowLeft}')
    expect(
      await screen.findByRole('img', { name: 'Fotografia 2 de 3 (verso.jpg)' }),
    ).toBeInTheDocument()
  })

  it('closes with Escape and returns focus to the thumbnail that opened it', async () => {
    renderGallery()
    const thumbnail = screen.getByRole('button', { name: 'Ampliar Fotografia 2 de 3 (verso.jpg)' })
    await userEvent.click(thumbnail)
    await userEvent.keyboard('{ArrowRight}')

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(thumbnail).toHaveFocus()
  })

  it('closes with the close button and returns focus to the thumbnail', async () => {
    renderGallery()
    const thumbnail = screen.getByRole('button', {
      name: 'Ampliar Fotografia 3 de 3 (lateral.jpg)',
    })
    await userEvent.click(thumbnail)

    await userEvent.click(screen.getByRole('button', { name: 'Fechar visualizador' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(thumbnail).toHaveFocus()
  })

  it('shows no previous/next navigation for a single photo', async () => {
    renderGallery([{ id: 'only', alt: 'Fotografia 1 de 1 (única.jpg)', fileName: 'única.jpg' }])
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 1 (única.jpg)' }),
    )

    expect(
      await screen.findByRole('img', { name: 'Fotografia 1 de 1 (única.jpg)' }),
    ).toBeInTheDocument()
    expect(screen.getByText('1 de 1')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Fotografia seguinte' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Fotografia anterior' })).not.toBeInTheDocument()
    // No empty navigation row: counter and zoom stay on one line.
    expect(screen.getByText('1 de 1').parentElement).not.toHaveClass('max-lg:w-full')
    expect(screen.getByRole('group', { name: 'Zoom' })).not.toHaveClass('max-lg:w-full')
    await userEvent.keyboard('{ArrowRight}')
    expect(screen.getByText('1 de 1')).toBeInTheDocument()
  })

  it('zooms in and out within bounds and resets to 100%', async () => {
    renderGallery()
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 3 (frente.jpg)' }),
    )
    const alt = 'Fotografia 1 de 3 (frente.jpg)'
    expect(await zoomOf(alt)).toBe('100%')

    const zoomIn = screen.getByRole('button', { name: 'Aumentar zoom' })
    await userEvent.click(zoomIn)
    expect(await zoomOf(alt)).toBe('125%')
    expect(screen.getByRole('button', { name: 'Repor zoom (atual 125%)' })).toHaveTextContent(
      '125%',
    )

    for (let i = 0; i < 10; i++) if (!zoomIn.hasAttribute('disabled')) await userEvent.click(zoomIn)
    expect(await zoomOf(alt)).toBe('300%')
    expect(zoomIn).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Repor zoom (atual 300%)' }))
    expect(await zoomOf(alt)).toBe('100%')

    const zoomOut = screen.getByRole('button', { name: 'Reduzir zoom' })
    await userEvent.click(zoomOut)
    await userEvent.click(zoomOut)
    expect(await zoomOf(alt)).toBe('50%')
    expect(zoomOut).toBeDisabled()
  })

  it('resets the zoom when changing photo', async () => {
    renderGallery()
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 3 (frente.jpg)' }),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Aumentar zoom' }))
    await userEvent.click(screen.getByRole('button', { name: 'Aumentar zoom' }))
    expect(await zoomOf('Fotografia 1 de 3 (frente.jpg)')).toBe('150%')

    await userEvent.keyboard('{ArrowRight}')
    expect(await zoomOf('Fotografia 2 de 3 (verso.jpg)')).toBe('100%')
    expect(screen.getByRole('button', { name: 'Repor zoom (atual 100%)' })).toBeInTheDocument()
  })

  it('loads every image only through the provided authenticated loader, as an object URL', async () => {
    const { getBlob } = renderGallery()
    await userEvent.click(
      screen.getByRole('button', { name: 'Ampliar Fotografia 1 de 3 (frente.jpg)' }),
    )

    const img = await screen.findByRole('img', { name: 'Fotografia 1 de 3 (frente.jpg)' })
    expect(getBlob).toHaveBeenCalledWith('a')
    expect(img.getAttribute('src')).toMatch(/^blob:/)
  })
})

describe('PhotoViewer — object URL lifetime', () => {
  function LoadedThumbnail({
    onOpen,
    getBlob,
  }: {
    onOpen: () => void
    getBlob: (id: string) => Promise<Blob>
  }) {
    const { url } = usePrivateImageUrl(['lifetime-blob', 'a'], () => getBlob('a'))
    return (
      <PhotoThumbnailButton label="Ampliar frente" onOpen={onOpen}>
        {url ? <img src={url} alt="" data-testid="thumb" /> : null}
      </PhotoThumbnailButton>
    )
  }

  function Harness({ getBlob }: { getBlob: (id: string) => Promise<Blob> }) {
    const [index, setIndex] = useState<number | null>(null)
    return (
      <>
        <LoadedThumbnail onOpen={() => setIndex(0)} getBlob={getBlob} />
        <PhotoViewer
          title="Fotografias atuais"
          photos={[photos[0]!]}
          index={index}
          onIndexChange={setIndex}
          blobKey={(id) => ['lifetime-blob', id]}
          getBlob={getBlob}
        />
      </>
    )
  }

  it('opening the viewer on an already-loaded photo (StrictMode) never shows a revoked URL', async () => {
    const revoked = new Set<string>()
    vi.stubGlobal(
      'URL',
      Object.assign(URL, {
        createObjectURL: () => `blob:lifetime-${++urlCounter}`,
        revokeObjectURL: (url: string) => revoked.add(url),
      }),
    )
    const getBlob = vi.fn(() => Promise.resolve(new Blob(['a'], { type: 'image/jpeg' })))
    render(
      <StrictMode>
        <QueryClientProvider
          client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
        >
          <Harness getBlob={getBlob} />
        </QueryClientProvider>
      </StrictMode>,
    )
    await screen.findByTestId('thumb')

    await userEvent.click(screen.getByRole('button', { name: 'Ampliar frente' }))
    const img = await screen.findByRole('img', { name: 'Fotografia 1 de 3 (frente.jpg)' })
    await new Promise((resolve) => setTimeout(resolve, 10))

    expect(revoked.has(img.getAttribute('src')!)).toBe(false)
    expect(revoked.has(screen.getByTestId('thumb').getAttribute('src')!)).toBe(false)
    // One authenticated download, shared by thumbnail and viewer.
    expect(getBlob).toHaveBeenCalledTimes(1)
  })
})
