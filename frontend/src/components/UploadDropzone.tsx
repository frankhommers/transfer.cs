import { useState, useCallback } from 'react'
import { useDropzone } from 'react-dropzone'
import { Upload, CheckCircle, Loader2, Copy, Check } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'

interface UploadResult {
  filename: string
  url: string
  deleteUrl: string
}

export function UploadDropzone() {
  const [uploading, setUploading] = useState(false)
  const [progress, setProgress] = useState(0)
  const [results, setResults] = useState<UploadResult[]>([])
  const [copiedIndex, setCopiedIndex] = useState<number | null>(null)

  const onDrop = useCallback(async (files: File[]) => {
    setUploading(true)
    setProgress(0)
    setResults([])

    const newResults: UploadResult[] = []

    for (let i = 0; i < files.length; i++) {
      const file = files[i]
      try {
        const response = await fetch(`/${encodeURIComponent(file.name)}`, {
          method: 'PUT',
          body: file,
        })
        const url = (await response.text()).trim()
        const deleteUrl = response.headers.get('X-Url-Delete') || ''
        newResults.push({ filename: file.name, url, deleteUrl })
      } catch {
        newResults.push({ filename: file.name, url: 'Upload failed', deleteUrl: '' })
      }
      setProgress(Math.round(((i + 1) / files.length) * 100))
    }

    setResults(newResults)
    setUploading(false)
  }, [])

  const { getRootProps, getInputProps, isDragActive } = useDropzone({ onDrop })

  const handleCopy = async (url: string, index: number) => {
    await navigator.clipboard.writeText(url)
    setCopiedIndex(index)
    setTimeout(() => setCopiedIndex(null), 2000)
  }

  return (
    <div className="space-y-4">
      <div
        {...getRootProps()}
        className={`border-2 border-dashed rounded-lg p-12 text-center cursor-pointer transition-colors ${
          isDragActive
            ? 'border-primary bg-primary/5'
            : 'border-muted-foreground/25 hover:border-primary/50'
        }`}
      >
        <input {...getInputProps()} />
        {uploading ? (
          <div className="space-y-4">
            <Loader2 className="h-12 w-12 mx-auto animate-spin text-primary" />
            <p className="text-muted-foreground">Uploading...</p>
            <Progress value={progress} className="max-w-xs mx-auto" />
          </div>
        ) : (
          <div className="space-y-2">
            <Upload className="h-12 w-12 mx-auto text-muted-foreground" />
            <p className="text-lg font-medium">
              {isDragActive ? 'Drop files here' : 'Drag & drop files here'}
            </p>
            <p className="text-sm text-muted-foreground">
              or click to select files
            </p>
          </div>
        )}
      </div>

      {results.length > 0 && (
        <div className="space-y-2">
          {results.map((result, index) => (
            <div
              key={index}
              className="flex items-center gap-3 bg-muted rounded-lg p-3"
            >
              <CheckCircle className="h-5 w-5 text-green-500 shrink-0" />
              <div className="flex-1 min-w-0 text-left">
                <p className="text-sm font-medium truncate">{result.filename}</p>
                <p className="text-xs text-muted-foreground truncate font-mono">
                  {result.url}
                </p>
              </div>
              <Button
                variant="ghost"
                size="icon"
                className="shrink-0 h-8 w-8"
                onClick={() => handleCopy(result.url, index)}
              >
                {copiedIndex === index ? (
                  <Check className="h-4 w-4 text-green-500" />
                ) : (
                  <Copy className="h-4 w-4" />
                )}
              </Button>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
