import {afterEach, describe, expect, it, vi} from 'vitest'
import {
  adminPasswordPath,
  parseAdminUrl,
  removeDownloadPassword,
  setDownloadPassword,
  validateDownloadPassword,
} from './downloadPassword'

const target = {token: 'abc', filename: 'my file#1.txt', adminToken: 'secret'}

describe('parseAdminUrl', () => {
  it('reads token, decoded filename and admin token', () => {
    expect(parseAdminUrl('https://files.example/admin/abc/my%20file%231.txt#secret')).toEqual(target)
  })

  it('accepts relative admin links', () => {
    expect(parseAdminUrl('/admin/abc/file.txt#secret')).toEqual({token: 'abc', filename: 'file.txt', adminToken: 'secret'})
  })

  it.each([
    '',
    'https://files.example/admin/abc/file.txt',
    'https://files.example/admin/abc/file.txt#',
    'https://files.example/abc/file.txt#secret',
    'https://files.example/admin/abc/dir/file.txt#secret',
    'https://files.example/admin/abc/%E0%A4%A.txt#secret',
  ])('rejects %j', (url) => {
    expect(parseAdminUrl(url)).toBeNull()
  })
})

describe('adminPasswordPath', () => {
  it('encodes token and filename', () => {
    expect(adminPasswordPath(target)).toBe('/api/admin/abc/my%20file%231.txt/password')
  })
})

describe('validateDownloadPassword', () => {
  it('accepts any non-empty password up to 1024 characters', () => {
    expect(validateDownloadPassword(' ')).toBeNull()
    expect(validateDownloadPassword('ä'.repeat(1024))).toBeNull()
  })

  it('rejects empty and overly long passwords', () => {
    expect(validateDownloadPassword('')).not.toBeNull()
    expect(validateDownloadPassword('a'.repeat(1025))).not.toBeNull()
  })
})

describe('password requests', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('sets the password with the admin bearer token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, {status: 204}))
    vi.stubGlobal('fetch', fetchMock)

    await setDownloadPassword(target, 'pässword')

    expect(fetchMock).toHaveBeenCalledWith('/api/admin/abc/my%20file%231.txt/password', {
      method: 'PUT',
      headers: {'Content-Type': 'application/json', Authorization: 'Bearer secret'},
      body: JSON.stringify({password: 'pässword'}),
      cache: 'no-store',
    })
  })

  it('removes the password with DELETE', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, {status: 204}))
    vi.stubGlobal('fetch', fetchMock)

    await removeDownloadPassword(target)

    expect(fetchMock.mock.calls[0][1]).toMatchObject({method: 'DELETE', headers: {Authorization: 'Bearer secret'}})
  })

  it.each([
    [404, '', 'File not found or admin link no longer valid.'],
    [400, '"The password must be 1 to 1024 characters."', 'The password must be 1 to 1024 characters.'],
    [500, 'boom', 'Request failed (HTTP 500)'],
  ])('reports HTTP %i as an error', async (status, body, message) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body || null, {status})))

    await expect(setDownloadPassword(target, 'x')).rejects.toThrow(message)
  })

  it('reports network failures', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('failed')))

    await expect(removeDownloadPassword(target)).rejects.toThrow('Network error')
  })
})
