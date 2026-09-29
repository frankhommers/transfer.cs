import {describe, expect, it} from 'vitest'
import {generatePassword, passwordAlphabet} from './passwordGenerator'

const forbidden = '1lI!|¦0OoØ2Zz5Ss6b8B9g.,-_`\'"/;{}()[]rnmvw\\><&$'

describe('generatePassword', () => {
  it('excludes ambiguous and shell-unsafe characters from the alphabet', () => {
    for (const char of forbidden) expect(passwordAlphabet).not.toContain(char)
    expect(passwordAlphabet).toHaveLength(49)
    expect(new Set(passwordAlphabet).size).toBe(passwordAlphabet.length)
  })

  it.each([8, 16, 32])('returns %i characters from the alphabet', (length) => {
    const password = generatePassword(length)
    expect(password).toHaveLength(length)
    for (const char of password) expect(passwordAlphabet).toContain(char)
  })

  it('stays within printable ASCII accepted by the upload form', () => {
    expect(generatePassword(64)).toMatch(/^[\x21-\x7E]+$/)
  })

  it('uses the whole alphabet', () => {
    const seen = new Set(generatePassword(1024))
    expect(seen.size).toBe(passwordAlphabet.length)
  })

  it('rejects lengths outside the supported range', () => {
    expect(() => generatePassword(0)).toThrow(RangeError)
    expect(() => generatePassword(1025)).toThrow(RangeError)
  })
})
