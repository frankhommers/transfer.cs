import {useState} from 'react'
import {Loader2} from 'lucide-react'
import {PasswordField} from '@/components/PasswordField'
import {Button} from '@/components/ui/button'
import {validateDownloadPassword} from '@/lib/downloadPassword'
import {defaultPasswordLength, generatePassword} from '@/lib/passwordGenerator'
import {cn} from '@/lib/utils'

interface PasswordEditorProps {
  onApply: (password: string) => Promise<void>
  onCancel: () => void
  className?: string
}

export function PasswordEditor({onApply, onCancel, className}: PasswordEditorProps) {
  const [password, setPassword] = useState(() => generatePassword(defaultPasswordLength))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const validationError = validateDownloadPassword(password)

  const handleChange = (value: string) => {
    setPassword(value)
    setError(null)
  }

  const handleApply = async () => {
    if (saving || validationError) return
    setSaving(true)
    setError(null)
    try {
      await onApply(password)
    } catch (applyError: unknown) {
      setError(applyError instanceof Error ? applyError.message : 'Could not save the password.')
      setSaving(false)
    }
  }

  const shownError = error ?? (password ? validationError : null)

  return (
    <div className={cn('space-y-1 text-left', className)}>
      <PasswordField
        value={password}
        onChange={handleChange}
        onSubmit={handleApply}
        onCancel={saving ? undefined : onCancel}
        disabled={saving}
        invalid={!!shownError}
        autoFocus
      >
        <div className="flex items-center gap-1">
          <Button size="sm" onClick={handleApply} disabled={saving || !!validationError}>
            {saving && <Loader2 className="animate-spin"/>} Apply
          </Button>
          <Button size="sm" variant="ghost" onClick={onCancel} disabled={saving}>Cancel</Button>
        </div>
      </PasswordField>
      {shownError && <p className="text-xs text-destructive break-words" role="alert">{shownError}</p>}
    </div>
  )
}
