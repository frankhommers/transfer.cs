import {useEffect, useState} from 'react'
import {Loader2} from 'lucide-react'
import {fetchQrCode, type ShareTarget} from '@/lib/qrCode'
import {cn} from '@/lib/utils'

interface ShareQrCodeProps {
  target: ShareTarget
  className?: string
}

export function ShareQrCode({target, className}: ShareQrCodeProps) {
  const {token, filename} = target
  const [qrCode, setQrCode] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    fetchQrCode({token, filename})
      .then((code) => !cancelled && setQrCode(code))
      .catch((reason: unknown) => !cancelled && setError(reason instanceof Error ? reason.message : 'Could not load the QR code'))
    return () => {
      cancelled = true
    }
  }, [token, filename])

  if (error) return <p className={cn('text-xs text-destructive', className)} role="alert">{error}</p>
  if (!qrCode) return <Loader2 className={cn('size-5 animate-spin text-muted-foreground', className)} aria-label="Loading QR code"/>
  return (
    <img
      src={`data:image/png;base64,${qrCode}`}
      alt={`QR code for the download link of ${filename}`}
      className={cn('size-40 rounded-md bg-white p-2', className)}
    />
  )
}
