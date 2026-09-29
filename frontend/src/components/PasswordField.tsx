import {useState, type KeyboardEvent, type ReactNode} from 'react'
import {Check, Copy, Dices, Eye, EyeOff} from 'lucide-react'
import {Button} from '@/components/ui/button'
import {Input} from '@/components/ui/input'
import {Slider} from '@/components/ui/slider'
import {copyToClipboard} from '@/lib/clipboard'
import {defaultPasswordLength, generatePassword, maxPasswordLength, minPasswordLength} from '@/lib/passwordGenerator'
import {cn} from '@/lib/utils'

interface PasswordFieldProps {
  value: string
  onChange: (value: string) => void
  onSubmit?: () => void
  onCancel?: () => void
  disabled?: boolean
  invalid?: boolean
  autoFocus?: boolean
  className?: string
  children?: ReactNode
}

export function PasswordField({
  value, onChange, onSubmit, onCancel, disabled, invalid, autoFocus, className, children,
}: PasswordFieldProps) {
  const [visible, setVisible] = useState(true)
  const [length, setLength] = useState(defaultPasswordLength)
  const [copied, setCopied] = useState(false)

  const handleLengthChange = (next: number | readonly number[]) => {
    const nextLength = Array.isArray(next) ? next[0] : next as number
    setLength(nextLength)
    onChange(generatePassword(nextLength))
  }

  const handleCopy = async () => {
    await copyToClipboard(value)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter' && onSubmit) {
      event.preventDefault()
      onSubmit()
    } else if (event.key === 'Escape' && onCancel) {
      event.preventDefault()
      onCancel()
    }
  }

  return (
    <div className={cn('flex flex-wrap items-center gap-x-3 gap-y-2', className)}>
      <div className="relative min-w-48 max-w-sm flex-1">
        <Input
          type={visible ? 'text' : 'password'}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Download password"
          aria-label="Download password"
          aria-invalid={invalid}
          autoComplete="new-password"
          spellCheck={false}
          autoFocus={autoFocus}
          disabled={disabled}
          className="bg-background pr-[4.75rem] font-mono"
        />
        <div className="absolute inset-y-0 right-1 flex items-center">
          <Button
            type="button"
            variant="ghost"
            size="icon-xs"
            onClick={() => setVisible((prev) => !prev)}
            aria-label={visible ? 'Hide password' : 'Show password'}
            title={visible ? 'Hide password' : 'Show password'}
          >
            {visible ? <EyeOff/> : <Eye/>}
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon-xs"
            onClick={handleCopy}
            disabled={!value}
            aria-label="Copy password"
            title="Copy password"
          >
            {copied ? <Check className="text-green-500"/> : <Copy/>}
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon-xs"
            onClick={() => onChange(generatePassword(length))}
            disabled={disabled}
            aria-label="Suggest password"
            title="Suggest password"
          >
            <Dices/>
          </Button>
        </div>
      </div>
      <div className="flex w-28 items-center gap-2" title="Suggested password length">
        <Slider
          value={[length]}
          min={minPasswordLength}
          max={maxPasswordLength}
          step={1}
          onValueChange={handleLengthChange}
          disabled={disabled}
          aria-label="Suggested password length"
          className="[&_[data-slot=slider-track]]:bg-border"
        />
        <span className="w-5 shrink-0 text-right text-xs tabular-nums text-muted-foreground">{length}</span>
      </div>
      {children}
    </div>
  )
}
