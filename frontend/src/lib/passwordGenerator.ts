export const passwordAlphabet = 'acdefhijkpqtuxyACDEFGHJKLMNPQRTUVWXY347#%*+:=?@^~'
export const minPasswordLength = 8
export const maxPasswordLength = 32
export const defaultPasswordLength = 16

const maxGeneratedLength = 1024
// Largest multiple of the alphabet size below 2^32; values at or above it are rejected so every character is equally likely.
const unbiasedLimit = Math.floor(2 ** 32 / passwordAlphabet.length) * passwordAlphabet.length

export function generatePassword(length: number): string {
  if (!Number.isInteger(length) || length < 1 || length > maxGeneratedLength) {
    throw new RangeError(`Password length must be between 1 and ${maxGeneratedLength}.`)
  }
  const chars: string[] = []
  const buffer = new Uint32Array(length)
  while (chars.length < length) {
    crypto.getRandomValues(buffer)
    for (const value of buffer) {
      if (value >= unbiasedLimit) continue
      chars.push(passwordAlphabet[value % passwordAlphabet.length])
      if (chars.length === length) break
    }
  }
  return chars.join('')
}
