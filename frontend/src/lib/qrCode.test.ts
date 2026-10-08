import {describe, expect, it} from 'vitest'
import {parseShareUrl, previewPath} from './qrCode'

describe('parseShareUrl', () => {
  it('extracts token and filename from a share link', () => {
    expect(parseShareUrl('https://files.example.com/abc123/report%20v2.pdf'))
      .toEqual({token: 'abc123', filename: 'report v2.pdf'})
  })

  it('keeps a proxy path prefix out of the result', () => {
    expect(parseShareUrl('https://example.com/transfer/abc123/a.txt')).toEqual({token: 'abc123', filename: 'a.txt'})
  })

  it('rejects links without token and filename', () => {
    expect(parseShareUrl('https://example.com/a.txt')).toBeNull()
    expect(parseShareUrl('not a url')).toBeNull()
  })
})

describe('previewPath', () => {
  it('encodes token and filename', () => {
    expect(previewPath({token: 'abc', filename: 'a b#.txt'})).toBe('/api/preview/abc/a%20b%23.txt')
  })
})
