import {useState, useCallback, useRef} from 'react'
import {useDropzone} from 'react-dropzone'
import {Upload, CheckCircle, XCircle, Loader2, Copy, Check, Clock, Trash2, Hash, ShieldCheck, KeyRound, RotateCcw, Lock, LockKeyhole, Eye, EyeOff} from 'lucide-react'
import {Progress} from '@/components/ui/progress'
import {Button} from '@/components/ui/button'
import {Input} from '@/components/ui/input'
import {Badge} from '@/components/ui/badge'
import {cn} from '@/lib/utils'

interface UploadResult {
  id: string
  files: File[]
  filename: string
  url: string
  deleteUrl: string
  adminUrl: string
  expires: string | null
  checksum: string
  passwordProtected: boolean
  password?: string
  failed: boolean
  error?: string
  retrying?: boolean
}

type UploadedFileResult = Pick<UploadResult, 'filename' | 'url' | 'deleteUrl' | 'adminUrl' | 'expires' | 'checksum' | 'passwordProtected'>

const maxPasswordLength = 1024

// Browsers can only send printable ASCII in a request header, and HTTP strips surrounding spaces.
function validatePassword(password: string): string | null {
  if (!password.trim()) return 'Enter a password.'
  if (password.length > maxPasswordLength) return `Use at most ${maxPasswordLength} characters.`
  if (!/^[\x20-\x7E]*$/.test(password)) return 'Use printable ASCII characters only.'
  if (password !== password.trim()) return 'Remove leading and trailing spaces.'
  return null
}

interface UploadProgress {
  loaded: number
  total: number
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

function verifyCommand(result: {checksum: string; filename: string}): string {
  const line = `${result.checksum}  ${result.filename}`.replace(/'/g, "'\\''")
  return `printf '%s\\n' '${line}' | shasum -a 256 -c`
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

function uploadFiles(
  files: File[],
  password: string | undefined,
  onProgress: (loaded: number, total: number) => void,
  onProcessing: () => void
): Promise<UploadedFileResult> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open(files.length > 1 ? 'POST' : 'PUT', files.length > 1 ? '/archive' : `/${encodeURIComponent(files[0].name)}`)
    xhr.setRequestHeader('Accept', 'application/json')
    if (password) xhr.setRequestHeader('Download-Password', password)

    xhr.upload.addEventListener('progress', (e) => {
      if (e.lengthComputable) {
        onProgress(e.loaded, e.total)
      }
    })

    xhr.upload.addEventListener('load', onProcessing)

    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          const {files} = JSON.parse(xhr.responseText) as {
            files: (Omit<UploadedFileResult, 'checksum'> & {sha256: string})[]
          }
          if (files.length !== 1 || !files[0].url) throw new Error('Invalid upload response')
          const {sha256, ...result} = files[0]
          resolve({...result, checksum: sha256, passwordProtected: result.passwordProtected === true})
        } catch {
          reject(new Error('Invalid upload response'))
        }
      } else {
        reject(new Error(xhr.responseText || xhr.statusText || `Upload failed (HTTP ${xhr.status})`))
      }
    })

    xhr.addEventListener('error', () => reject(new Error('Network error')))
    xhr.addEventListener('abort', () => reject(new Error('Upload aborted')))

    if (files.length > 1) {
      const form = new FormData()
      for (const file of files) form.append('file', file, file.name)
      xhr.send(form)
    } else {
      xhr.send(files[0])
    }
  })
}

export function UploadDropzone() {
  const busy = useRef(false)
  const nextUploadId = useRef(0)
  const [uploading, setUploading] = useState(false)
  const [processing, setProcessing] = useState(false)
  const [creatingZip, setCreatingZip] = useState(false)
  const [progress, setProgress] = useState<UploadProgress>({loaded: 0, total: 0})
  const [results, setResults] = useState<UploadResult[]>([])
  const [copiedIndex, setCopiedIndex] = useState<number | null>(null)
  const [copiedChecksumIndex, setCopiedChecksumIndex] = useState<number | null>(null)
  const [copiedAdminIndex, setCopiedAdminIndex] = useState<number | null>(null)
  const [copiedPasswordIndex, setCopiedPasswordIndex] = useState<number | null>(null)
  const [copiedAll, setCopiedAll] = useState(false)
  const [protect, setProtect] = useState(false)
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const passwordError = protect ? validatePassword(password) : null
  const uploadPassword = protect && !passwordError ? password : undefined

  const onDrop = useCallback(async (files: File[]) => {
    if (busy.current || files.length === 0) return
    if (protect && !uploadPassword) return
    busy.current = true
    setUploading(true)
    setProcessing(false)
    setCreatingZip(files.length > 1)
    const filename = files.length > 1 ? 'files.zip' : files[0].name
    const id = String(nextUploadId.current++)
    setProgress({loaded: 0, total: files.reduce((sum, file) => sum + file.size, 0)})

    try {
      const result = await uploadFiles(files, uploadPassword, (loaded, total) => {
        setProgress({loaded, total})
      }, () => setProcessing(true))
      setResults((prev) => [...prev, {id, files, ...result, password: uploadPassword, failed: false}])
    } catch (error: unknown) {
      setResults((prev) => [...prev, {
        id, files, error: error instanceof Error ? error.message : 'Upload failed',
        filename, url: '', deleteUrl: '', adminUrl: '', expires: null, checksum: '',
        passwordProtected: false, password: uploadPassword, failed: true,
      }])
    } finally {
      busy.current = false
      setUploading(false)
      setProcessing(false)
    }
  }, [protect, uploadPassword])

  const {getRootProps, getInputProps, isDragActive} = useDropzone({
    onDrop, disabled: uploading || !!passwordError, multiple: true,
  })

  const handleRetry = async (result: UploadResult) => {
    if (busy.current) return
    busy.current = true
    setUploading(true)
    setProcessing(false)
    setCreatingZip(result.files.length > 1)
    setProgress({loaded: 0, total: result.files.reduce((sum, file) => sum + file.size, 0)})
    setResults((prev) => prev.map((item) => item.id === result.id ? {...item, retrying: true} : item))
    try {
      const uploaded = await uploadFiles(result.files, result.password, (loaded, total) => {
        setProgress({loaded, total})
      }, () => setProcessing(true))
      setResults((prev) => prev.map((item) => item.id === result.id
        ? {...item, ...uploaded, failed: false, retrying: false, error: undefined} : item))
    } catch (error: unknown) {
      setResults((prev) => prev.map((item) => item.id === result.id
        ? {...item, retrying: false, error: error instanceof Error ? error.message : 'Upload failed'} : item))
    } finally {
      busy.current = false
      setUploading(false)
      setProcessing(false)
    }
  }

  const handleCopyAll = async () => {
    await copyToClipboard(results.filter((result) => !result.failed).map((result) => result.url).join('\n'))
    setCopiedAll(true)
    setTimeout(() => setCopiedAll(false), 2000)
  }

  const handleCopy = async (url: string, index: number) => {
    await copyToClipboard(url)
    setCopiedIndex(index)
    setTimeout(() => setCopiedIndex(null), 2000)
  }

  const handleCopyChecksum = async (result: UploadResult, index: number) => {
    await copyToClipboard(verifyCommand(result))
    setCopiedChecksumIndex(index)
    setTimeout(() => setCopiedChecksumIndex(null), 2000)
  }

  const handleCopyAdmin = async (adminUrl: string, index: number) => {
    await copyToClipboard(adminUrl)
    setCopiedAdminIndex(index)
    setTimeout(() => setCopiedAdminIndex(null), 2000)
  }

  const handleCopyWithPassword = async (result: UploadResult, index: number) => {
    await copyToClipboard(`${result.url}\nPassword: ${result.password ?? ''}`)
    setCopiedPasswordIndex(index)
    setTimeout(() => setCopiedPasswordIndex(null), 2000)
  }

  const handleDelete = async (result: UploadResult) => {
    if (!result.deleteUrl) return
    try {
      const path = new URL(result.deleteUrl).pathname
      const response = await fetch(path, {method: 'DELETE'})
      if (!response.ok) throw new Error(`Deletion failed (HTTP ${response.status})`)
      setResults((prev) => prev.filter((item) => item.id !== result.id))
    } catch { /* ignore */
    }
  }

  const {loaded: totalLoaded, total: totalSize} = progress
  const overallPercent = totalSize > 0 ? Math.round((totalLoaded / totalSize) * 100) : 0

  return (
    <div className="space-y-4">
      <div
        {...getRootProps()}
        className={cn('border-2 border-dashed rounded-md p-12 text-center transition-colors',
          uploading ? 'cursor-wait' : passwordError ? 'cursor-not-allowed opacity-60' : 'cursor-pointer',
          isDragActive
            ? 'border-primary bg-primary/5'
            : 'border-muted-foreground/25 hover:border-primary/50'
        )}
      >
        <input {...getInputProps()} />
        {uploading ? (
          <div className="space-y-4">
            <Loader2 className="h-12 w-12 mx-auto animate-spin text-primary"/>
            <p className="text-muted-foreground" role="status">
              {processing ? (creatingZip ? 'Creating ZIP...' : 'Finishing upload...') : `Uploading... ${overallPercent}%`}
            </p>
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
            <p className="text-xs text-muted-foreground">
              Multiple files selected or dropped at once are combined into one ZIP with one download link.
            </p>
            <p className="text-xs text-muted-foreground">Separate uploads get separate links.</p>
          </div>
        )}
      </div>

      <div className="space-y-2 text-left">
        <label className="flex w-fit cursor-pointer items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="accent-primary"
            checked={protect}
            disabled={uploading}
            onChange={(e) => setProtect(e.target.checked)}
          />
          <Lock className="h-4 w-4 text-muted-foreground"/>
          Protect with password
        </label>
        {protect && (
          <div className="space-y-1">
            <div className="flex max-w-sm items-center gap-2">
              <Input
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Download password"
                aria-label="Download password"
                aria-invalid={password !== '' && !!passwordError}
                autoComplete="new-password"
                disabled={uploading}
              />
              <Button
                type="button"
                variant="outline"
                size="icon"
                onClick={() => setShowPassword((prev) => !prev)}
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                title={showPassword ? 'Hide password' : 'Show password'}
              >
                {showPassword ? <EyeOff/> : <Eye/>}
              </Button>
            </div>
            <p className={cn('text-xs', passwordError && password !== '' ? 'text-destructive' : 'text-muted-foreground')}>
              {passwordError ?? 'Recipients need this password to download. Share it separately from the link.'}
            </p>
          </div>
        )}
      </div>

      {results.length > 0 && (
        <div className="space-y-2">
          {results.filter((result) => !result.failed).length > 1 && (
            <Button variant="outline" onClick={handleCopyAll}>
              {copiedAll ? <Check/> : <Copy/>} {copiedAll ? 'Copied' : 'Copy all download links'}
            </Button>
          )}
          {results.map((result, index) => (
            <div
              key={result.id}
              className="flex items-center gap-3 bg-muted border border-border rounded-md p-3"
            >
              {result.failed ? (
                <XCircle className="h-5 w-5 text-destructive shrink-0"/>
              ) : (
                <CheckCircle className="h-5 w-5 text-green-500 shrink-0"/>
              )}
              <div className="flex-1 min-w-0 text-left">
                <div className="flex min-w-0 items-center gap-2">
                  <p className="text-sm font-medium truncate">{result.filename}</p>
                  {result.passwordProtected && (
                    <Badge variant="secondary" className="shrink-0" title="Password protected">
                      <Lock/> Password
                    </Badge>
                  )}
                </div>
                {result.files.length > 1 && (
                  <p className="text-xs text-muted-foreground">{result.files.length} files in one ZIP</p>
                )}
                {result.failed ? (
                  <p className="text-xs text-destructive break-words">{result.error || 'Upload failed'}</p>
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
                    {result.checksum && (
                      <p className="text-xs text-muted-foreground flex items-center gap-1 mt-0.5 min-w-0">
                        <Hash className="h-3 w-3 shrink-0"/>
                        <span className="font-mono truncate" title={`sha256:${result.checksum}`}>
                          {result.checksum}
                        </span>
                      </p>
                    )}
                  </>
                )}
              </div>
              {result.failed && (
                <Button variant="outline" disabled={uploading} onClick={() => handleRetry(result)}>
                  {result.retrying ? <Loader2 className="animate-spin"/> : <RotateCcw/>} Retry
                </Button>
              )}
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
                  {result.passwordProtected && result.password && (
                    <button
                      type="button"
                      className="p-2 rounded-md text-muted-foreground hover:text-foreground hover:bg-background transition-colors"
                      onClick={() => handleCopyWithPassword(result, index)}
                      aria-label="Copy link and password"
                      title="Copy link + password"
                    >
                      {copiedPasswordIndex === index ? (
                        <Check className="h-4 w-4 text-green-500"/>
                      ) : (
                        <LockKeyhole className="h-4 w-4"/>
                      )}
                    </button>
                  )}
                  {result.checksum && (
                    <button
                      type="button"
                      className="p-2 rounded-md text-muted-foreground hover:text-foreground hover:bg-background transition-colors"
                      onClick={() => handleCopyChecksum(result, index)}
                      aria-label="Copy checksum verify command"
                      title="Copy verify command"
                    >
                      {copiedChecksumIndex === index ? (
                        <Check className="h-4 w-4 text-green-500"/>
                      ) : (
                        <ShieldCheck className="h-4 w-4"/>
                      )}
                    </button>
                  )}
                  {result.adminUrl && (
                    <button
                      type="button"
                      className="p-2 rounded-md text-muted-foreground hover:text-foreground hover:bg-background transition-colors"
                      onClick={() => handleCopyAdmin(result.adminUrl, index)}
                      aria-label="Copy private admin link"
                      title="Copy private admin link"
                    >
                      {copiedAdminIndex === index ? (
                        <Check className="h-4 w-4 text-green-500"/>
                      ) : (
                        <KeyRound className="h-4 w-4"/>
                      )}
                    </button>
                  )}
                  {result.deleteUrl && (
                    <button
                      type="button"
                      className="p-2 rounded-md text-muted-foreground hover:text-destructive hover:bg-background transition-colors"
                      onClick={() => handleDelete(result)}
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
