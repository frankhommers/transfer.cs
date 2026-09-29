export const maxDownloadPasswordLength = 1024

export interface AdminTarget {
  token: string
  filename: string
  adminToken: string
}

export function validateDownloadPassword(password: string): string | null {
  if (!password) return 'Enter a password.'
  if (password.length > maxDownloadPasswordLength) return `Use at most ${maxDownloadPasswordLength} characters.`
  return null
}

export function parseAdminUrl(adminUrl: string): AdminTarget | null {
  let url: URL
  try {
    url = new URL(adminUrl, 'http://localhost')
  } catch {
    return null
  }
  const match = /^\/admin\/([^/]+)\/([^/]+)$/.exec(url.pathname)
  const adminToken = url.hash.slice(1)
  if (!match || !adminToken) return null
  try {
    return {token: decodeURIComponent(match[1]), filename: decodeURIComponent(match[2]), adminToken}
  } catch {
    return null
  }
}

export function adminPasswordPath({token, filename}: Pick<AdminTarget, 'token' | 'filename'>): string {
  return `/api/admin/${encodeURIComponent(token)}/${encodeURIComponent(filename)}/password`
}

async function readErrorMessage(response: Response): Promise<string> {
  const text = await response.text()
  try {
    const parsed: unknown = JSON.parse(text)
    if (typeof parsed === 'string') return parsed
  } catch {
    return text
  }
  return text
}

async function sendPasswordRequest(target: AdminTarget, init: RequestInit) {
  let response: Response
  try {
    response = await fetch(adminPasswordPath(target), {
      ...init,
      headers: {...init.headers, Authorization: `Bearer ${target.adminToken}`},
      cache: 'no-store',
    })
  } catch {
    throw new Error('Network error')
  }
  if (response.ok) return
  if (response.status === 404) throw new Error('File not found or admin link no longer valid.')
  const message = response.status === 400 ? await readErrorMessage(response) : ''
  throw new Error(message || `Request failed (HTTP ${response.status})`)
}

export function setDownloadPassword(target: AdminTarget, password: string) {
  return sendPasswordRequest(target, {
    method: 'PUT',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({password}),
  })
}

export function removeDownloadPassword(target: AdminTarget) {
  return sendPasswordRequest(target, {method: 'DELETE'})
}
