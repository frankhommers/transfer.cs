import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { Download, QrCode, FileIcon } from 'lucide-react'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

interface PreviewData {
  contentType: string
  filename: string
  url: string
  downloadUrl: string
  token: string
  hostname: string
  contentLength: number
  qrCode: string
  previewType: 'image' | 'video' | 'audio' | 'markdown' | 'text' | 'generic'
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 Bytes'
  const k = 1024
  const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i]
}

export function PreviewPage() {
  const { token, filename } = useParams<{ token: string; filename: string }>()
  const [preview, setPreview] = useState<PreviewData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showQr, setShowQr] = useState(false)
  const [textContent, setTextContent] = useState<string | null>(null)

  useEffect(() => {
    async function fetchPreview() {
      try {
        const res = await fetch(`/api/preview/${token}/${filename}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const data: PreviewData = await res.json()
        setPreview(data)

        if (data.previewType === 'text' || data.previewType === 'markdown') {
          const textRes = await fetch(`/inline/${token}/${filename}`)
          if (textRes.ok) {
            setTextContent(await textRes.text())
          }
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Failed to load preview')
      } finally {
        setLoading(false)
      }
    }

    fetchPreview()
  }, [token, filename])

  if (loading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <p className="text-muted-foreground">Loading...</p>
      </div>
    )
  }

  if (error || !preview) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <p className="text-destructive">{error || 'File not found'}</p>
      </div>
    )
  }

  const inlineUrl = `/inline/${token}/${filename}`

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-4xl mx-auto px-4 py-8">
        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div className="space-y-2">
                <CardTitle className="text-2xl break-all">
                  {preview.filename}
                </CardTitle>
                <div className="flex flex-wrap gap-2">
                  <Badge variant="secondary">{preview.contentType}</Badge>
                  <Badge variant="outline">
                    {formatBytes(preview.contentLength)}
                  </Badge>
                </div>
              </div>
              <div className="flex gap-2 shrink-0">
                {preview.qrCode && (
                  <Button
                    variant="outline"
                    size="icon"
                    onClick={() => setShowQr(!showQr)}
                    title="Toggle QR code"
                  >
                    <QrCode className="h-4 w-4" />
                  </Button>
                )}
                <a
                  href={preview.downloadUrl}
                  className={cn(buttonVariants({ variant: 'default' }))}
                >
                  <Download className="h-4 w-4 mr-2" />
                  Download
                </a>
              </div>
            </div>

            {showQr && preview.qrCode && (
              <div className="mt-4 flex justify-center">
                <img
                  src={`data:image/png;base64,${preview.qrCode}`}
                  alt="QR Code"
                  className="w-48 h-48"
                />
              </div>
            )}
          </CardHeader>

          <CardContent>
            <PreviewContent
              previewType={preview.previewType}
              inlineUrl={inlineUrl}
              textContent={textContent}
            />
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

function PreviewContent({
  previewType,
  inlineUrl,
  textContent,
}: {
  previewType: PreviewData['previewType']
  inlineUrl: string
  textContent: string | null
}) {
  switch (previewType) {
    case 'image':
      return (
        <div className="flex justify-center">
          <img
            src={inlineUrl}
            alt="Preview"
            className="max-w-full max-h-[70vh] rounded-lg"
          />
        </div>
      )
    case 'video':
      return (
        <video
          src={inlineUrl}
          controls
          className="w-full max-h-[70vh] rounded-lg"
        />
      )
    case 'audio':
      return (
        <audio src={inlineUrl} controls className="w-full" />
      )
    case 'text':
    case 'markdown':
      return (
        <pre className="bg-muted rounded-lg p-4 overflow-x-auto text-sm font-mono whitespace-pre-wrap break-words max-h-[70vh] overflow-y-auto">
          {textContent ?? 'Loading...'}
        </pre>
      )
    default:
      return (
        <div className="flex flex-col items-center justify-center py-12 text-muted-foreground">
          <FileIcon className="h-16 w-16 mb-4" />
          <p>No preview available</p>
        </div>
      )
  }
}
