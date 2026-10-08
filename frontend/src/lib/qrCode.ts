export interface ShareTarget {
  token: string
  filename: string
}

export function parseShareUrl(shareUrl: string): ShareTarget | null {
  let url: URL
  try {
    url = new URL(shareUrl)
  } catch {
    return null
  }
  const segments = url.pathname.split('/').filter(Boolean)
  if (segments.length < 2) return null
  try {
    return {token: decodeURIComponent(segments[segments.length - 2]), filename: decodeURIComponent(segments[segments.length - 1])}
  } catch {
    return null
  }
}

export function previewPath({token, filename}: ShareTarget): string {
  return `/api/preview/${encodeURIComponent(token)}/${encodeURIComponent(filename)}`
}

export async function fetchQrCode(target: ShareTarget): Promise<string> {
  const response = await fetch(previewPath(target), {cache: 'no-store'})
  if (!response.ok) throw new Error(`Could not load the QR code (HTTP ${response.status})`)
  const {qrCode} = await response.json() as {qrCode?: string}
  if (!qrCode) throw new Error('Could not load the QR code')
  return qrCode
}
