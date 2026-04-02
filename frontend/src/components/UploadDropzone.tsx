import {useState, useCallback} from 'react'
import {useDropzone} from 'react-dropzone'
import {Upload, CheckCircle, XCircle, Loader2, Copy, Check, Clock, Trash2} from 'lucide-react'
import {Progress} from '@/components/ui/progress'

interface UploadResult {
  filename: string
  url: string
  deleteUrl: string
  expires: string | null
  failed: boolean
}

interface FileProgress {
  filename: string
  loaded: number
  total: number
  done: boolean
}

function formatExpiry(expires: string): string {
  const ms = new Date(expires).getTime() - Date.now()
  if (ms <= 0) return 'Expired'
  const seconds = Math.floor(ms / 1000)
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)

  if (days > 0) return `Expires in ${days} day${days === 1 ? '' : 's'}`
  if (hours > 0) return `Expires in ${hours} hour${hours === 1 ? '' : 's'}`
  if (minutes > 0) return `Expires in ${minutes} minute${minutes === 1 ? '' : 's'}`
  return `Expires in ${seconds} second${seconds === 1 ? '' : 's'}`
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return `${(bytes / Math.pow(1024, i)).toFixed(i > 0 ? 1 : 0)} ${units[i]}`
}

async function copyToClipboard(text: string) {
  try {
    await navigator.clipboard.writeText(text)
  } catch {
    const textArea = document.createElement('textarea')
    textArea.value = text
    textArea.style.position = 'fixed'
    textArea.style.opacity = '0'
    document.body.appendChild(textArea)
    textArea.select()
    document.execCommand('copy')
    document.body.removeChild(textArea)
  }
}

function uploadFile(
  file: File,
  onProgress: (loaded: number, total: number) => void
): Promise<{ url: string; deleteUrl: string; expires: string | null }> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', `/${encodeURIComponent(file.name)}`)

    xhr.upload.addEventListener('progress', (e) => {
      if (e.lengthComputable) {
        onProgress(e.loaded, e.total)
      }
    })

    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve({
          url: xhr.responseText.trim(),
          deleteUrl: xhr.getResponseHeader('X-Url-Delete') || '',
          expires: xhr.getResponseHeader('Expires'),
        })
      } else {
        reject(new Error(xhr.statusText))
      }
    })

    xhr.addEventListener('error', () => reject(new Error('Network error')))
    xhr.addEventListener('abort', () => reject(new Error('Upload aborted')))

    xhr.send(file)
  })
}

export function UploadDropzone() {
  const [uploading, setUploading] = useState(false)
  const [fileProgress, setFileProgress] = useState<FileProgress[]>([])
  const [results, setResults] = useState<UploadResult[]>([])
  const [copiedIndex, setCopiedIndex] = useState<number | null>(null)

  const onDrop = useCallback(async (files: File[]) => {
    setUploading(true)
    setResults([])
    setFileProgress(files.map((f) => ({filename: f.name, loaded: 0, total: f.size, done: false})))

    const newResults: UploadResult[] = []

    for (let i = 0; i < files.length; i++) {
      const file = files[i]
      try {
        const result = await uploadFile(file, (loaded, total) => {
          setFileProgress((prev) =>
            prev.map((p, idx) => idx === i ? {...p, loaded, total} : p)
          )
        })
        setFileProgress((prev) =>
          prev.map((p, idx) => idx === i ? {...p, done: true, loaded: p.total} : p)
        )
        newResults.push({filename: file.name, ...result, failed: false})
      } catch {
        setFileProgress((prev) =>
          prev.map((p, idx) => idx === i ? {...p, done: true} : p)
        )
        newResults.push({filename: file.name, url: '', deleteUrl: '', expires: null, failed: true})
      }
    }

    setResults(newResults)
    setUploading(false)
  }, [])

  const {getRootProps, getInputProps, isDragActive} = useDropzone({onDrop})

  const handleCopy = async (url: string, index: number) => {
    await copyToClipboard(url)
    setCopiedIndex(index)
    setTimeout(() => setCopiedIndex(null), 2000)
  }

  const handleDelete = async (result: UploadResult, index: number) => {
    if (!result.deleteUrl) return
    try {
      const path = new URL(result.deleteUrl).pathname
      await fetch(path, {method: 'DELETE'})
      setResults((prev) => prev.filter((_, i) => i !== index))
    } catch { /* ignore */
    }
  }

  const totalLoaded = fileProgress.reduce((sum, p) => sum + p.loaded, 0)
  const totalSize = fileProgress.reduce((sum, p) => sum + p.total, 0)
  const overallPercent = totalSize > 0 ? Math.round((totalLoaded / totalSize) * 100) : 0

  return (
    <div className="space-y-4">
      <div
        {...getRootProps()}
        className={`border-2 border-dashed rounded-md p-12 text-center cursor-pointer transition-colors ${
          isDragActive
            ? 'border-primary bg-primary/5'
            : 'border-muted-foreground/25 hover:border-primary/50'
        }`}
      >
        <input {...getInputProps()} />
        {uploading ? (
          <div className="space-y-4">
            <Loader2 className="h-12 w-12 mx-auto animate-spin text-primary"/>
            <p className="text-muted-foreground">Uploading... {overallPercent}%</p>
            <Progress value={overallPercent} className="max-w-xs mx-auto"/>
            <p className="text-xs text-muted-foreground">
              {formatBytes(totalLoaded)} / {formatBytes(totalSize)}
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            <Upload className="h-12 w-12 mx-auto text-muted-foreground"/>
            <p className="text-lg font-medium">
              {isDragActive ? 'Drop files here' : 'Drag & drop files here'}
            </p>
            <p className="text-sm text-muted-foreground">
              or click to select files
            </p>
          </div>
        )}
      </div>

      {/* Per-file progress during upload */}
      {uploading && fileProgress.length > 1 && (
        <div className="space-y-1">
          {fileProgress.map((fp, i) => {
            const pct = fp.total > 0 ? Math.round((fp.loaded / fp.total) * 100) : 0
            return (
              <div key={i} className="flex items-center gap-2 text-xs text-muted-foreground">
                <span className="truncate w-40">{fp.filename}</span>
                <Progress value={pct} className="flex-1 h-1.5"/>
                <span className="w-16 text-right">{fp.done ? 'Done' : `${pct}%`}</span>
              </div>
            )
          })}
        </div>
      )}

      {results.length > 0 && (
        <div className="space-y-2">
          {results.map((result, index) => (
            <div
              key={index}
              className="flex items-center gap-3 bg-muted border border-border rounded-md p-3"
            >
              {result.failed ? (
                <XCircle className="h-5 w-5 text-destructive shrink-0"/>
              ) : (
                <CheckCircle className="h-5 w-5 text-green-500 shrink-0"/>
              )}
              <div className="flex-1 min-w-0 text-left">
                <p className="text-sm font-medium truncate">{result.filename}</p>
                {result.failed ? (
                  <p className="text-xs text-destructive">Upload failed</p>
                ) : (
                  <>
                    <p className="text-xs text-muted-foreground truncate font-mono">
                      {result.url}
                    </p>
                    {result.expires && (
                      <p className="text-xs text-muted-foreground flex items-center gap-1 mt-0.5">
                        <Clock className="h-3 w-3"/>
                        {formatExpiry(result.expires)}
                      </p>
                    )}
                  </>
                )}
              </div>
              {!result.failed && (
                <div className="flex items-center gap-1 shrink-0">
                  <button
                    type="button"
                    className="p-2 rounded-md text-muted-foreground hover:text-foreground hover:bg-background transition-colors"
                    onClick={() => handleCopy(result.url, index)}
                    aria-label="Copy URL"
                  >
                    {copiedIndex === index ? (
                      <Check className="h-4 w-4 text-green-500"/>
                    ) : (
                      <Copy className="h-4 w-4"/>
                    )}
                  </button>
                  {result.deleteUrl && (
                    <button
                      type="button"
                      className="p-2 rounded-md text-muted-foreground hover:text-destructive hover:bg-background transition-colors"
                      onClick={() => handleDelete(result, index)}
                      aria-label="Delete file"
                    >
                      <Trash2 className="h-4 w-4"/>
                    </button>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
