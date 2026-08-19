import {useEffect, useState} from 'react'
import {useNavigate, useParams} from 'react-router-dom'
import {
  CalendarClock,
  Download,
  FileKey2,
  Fingerprint,
  HardDrive,
  LoaderCircle,
  Network,
  ShieldAlert,
  Trash2,
} from 'lucide-react'
import {Button} from '@/components/ui/button'
import {Card, CardContent, CardHeader, CardTitle} from '@/components/ui/card'

interface DownloadEntry {
  ipAddress: string
  downloadedAt: string
}

interface AdminMetadata {
  filename: string
  contentLength: number
  contentType: string
  sha256: string
  downloads: number
  maxDownloads: number
  maxDate: string
  downloadLogTotal: number
  downloadLog: DownloadEntry[]
}

type PageState =
  | {status: 'loading'}
  | {status: 'ready'; metadata: AdminMetadata}
  | {status: 'missing'}
  | {status: 'error'}
  | {status: 'deleted'}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const unit = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1)
  return `${(bytes / 1024 ** unit).toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(new Date(value))
}

function readAdminToken(storageKey: string): string {
  const fragment = window.location.hash.slice(1)
  if (fragment) {
    sessionStorage.setItem(storageKey, fragment)
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`)
    return fragment
  }
  return sessionStorage.getItem(storageKey) ?? ''
}

export function AdminPage() {
  const {token = '', filename = ''} = useParams()
  const navigate = useNavigate()
  const storageKey = `transfer.cs:admin:${token}/${filename}`
  const [adminToken] = useState(() => readAdminToken(storageKey))
  const [state, setState] = useState<PageState>(adminToken ? {status: 'loading'} : {status: 'missing'})
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (!adminToken) {
      return
    }

    const controller = new AbortController()
    fetch(`/api/admin/${encodeURIComponent(token)}/${encodeURIComponent(filename)}`, {
      headers: {'Admin-Token': adminToken},
      signal: controller.signal,
      cache: 'no-store',
    }).then(async (response) => {
      if (response.ok) {
        setState({status: 'ready', metadata: await response.json() as AdminMetadata})
      } else if (response.status === 404) {
        setState({status: 'missing'})
      } else {
        setState({status: 'error'})
      }
    }).catch((error: unknown) => {
      if (!(error instanceof DOMException && error.name === 'AbortError'))
        setState({status: 'error'})
    })

    return () => controller.abort()
  }, [adminToken, filename, token])

  const deleteFile = async () => {
    const response = await fetch(`/api/admin/${encodeURIComponent(token)}/${encodeURIComponent(filename)}`, {
      method: 'DELETE',
      headers: {'Admin-Token': adminToken},
      cache: 'no-store',
    })
    if (response.ok) {
      sessionStorage.removeItem(storageKey)
      setState({status: 'deleted'})
    } else if (response.status === 404) {
      setState({status: 'missing'})
    } else {
      setState({status: 'error'})
    }
  }

  if (state.status !== 'ready') {
    const content = {
      loading: ['Verifying capability', 'Reading private file metadata...'],
      missing: ['Nothing to reveal', 'This file does not exist, or the private capability is invalid.'],
      error: ['Request failed', 'The admin endpoint could not be reached. Try again later.'],
      deleted: ['File erased', 'The payload, metadata, and download history have been removed.'],
    }[state.status]

    return (
      <main className="min-h-screen grid place-items-center bg-background px-4">
        <div className="w-full max-w-lg border border-border bg-card p-8">
          {state.status === 'loading'
            ? <LoaderCircle className="mb-6 size-7 animate-spin text-primary"/>
            : <ShieldAlert className="mb-6 size-7 text-muted-foreground"/>}
          <p className="mb-2 text-xs uppercase tracking-[0.24em] text-muted-foreground">private control plane</p>
          <h1 className="mb-3 text-2xl font-semibold">{content[0]}</h1>
          <p className="text-sm text-muted-foreground">{content[1]}</p>
          {state.status !== 'loading' && (
            <Button className="mt-8" variant="outline" onClick={() => navigate('/')}>Return home</Button>
          )}
        </div>
      </main>
    )
  }

  const {metadata} = state
  const hasExpiry = new Date(metadata.maxDate).getUTCFullYear() > 1
  const downloadUrl = `/${encodeURIComponent(token)}/${encodeURIComponent(filename)}`
  const retainedCount = metadata.downloadLog.length

  return (
    <main className="min-h-screen bg-background px-4 py-16">
      <div className="mx-auto max-w-5xl">
        <header className="mb-10 border-l-2 border-primary pl-5">
          <div className="mb-2 flex items-center gap-2 text-xs uppercase tracking-[0.24em] text-primary">
            <FileKey2 className="size-4"/>
            private control plane
          </div>
          <h1 className="break-all text-3xl font-semibold tracking-tight">{metadata.filename}</h1>
          <p className="mt-2 text-sm text-muted-foreground">Capability verified. This view is not publicly discoverable.</p>
        </header>

        <section className="mb-6 grid gap-px bg-border sm:grid-cols-2 lg:grid-cols-4">
          <Metric icon={HardDrive} label="Size" value={formatBytes(metadata.contentLength)}/>
          <Metric icon={Download} label="Downloads" value={metadata.maxDownloads < 0
            ? `${metadata.downloads} / unlimited`
            : `${metadata.downloads} / ${metadata.maxDownloads}`}/>
          <Metric icon={Network} label="IP records" value={`${metadata.downloadLogTotal} total`}/>
          <Metric icon={CalendarClock} label="Expires" value={hasExpiry ? formatDate(metadata.maxDate) : 'never'}/>
        </section>

        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
          <Card>
            <CardHeader className="border-b">
              <CardTitle className="flex items-center gap-2">
                <Network className="size-4"/>
                Download history
              </CardTitle>
              <p className="text-muted-foreground">
                Showing {retainedCount} retained {retainedCount === 1 ? 'entry' : 'entries'} of {metadata.downloadLogTotal}.
              </p>
            </CardHeader>
            <CardContent className="px-0">
              {metadata.downloadLog.length === 0 ? (
                <p className="px-4 py-8 text-center text-muted-foreground">No IP addresses recorded.</p>
              ) : (
                <div className="divide-y divide-border">
                  {[...metadata.downloadLog].reverse().map((entry, index) => (
                    <div key={`${entry.downloadedAt}-${index}`} className="grid gap-1 px-4 py-3 sm:grid-cols-[1fr_auto] sm:items-center">
                      <span className="font-mono text-sm">{entry.ipAddress}</span>
                      <time className="text-muted-foreground" dateTime={entry.downloadedAt}>
                        {formatDate(entry.downloadedAt)}
                      </time>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>

          <aside className="space-y-4">
            <Card>
              <CardHeader className="border-b"><CardTitle>File identity</CardTitle></CardHeader>
              <CardContent className="space-y-4">
                <Detail label="Content type" value={metadata.contentType}/>
                <Detail label="SHA-256" value={metadata.sha256 || 'not available'} mono/>
                <a
                  href={downloadUrl}
                  rel="noreferrer"
                  className="inline-flex h-8 w-full items-center justify-center gap-1.5 bg-primary px-2.5 text-xs font-medium text-primary-foreground hover:bg-primary/80"
                >
                  <Download className="size-4"/> Download file
                </a>
              </CardContent>
            </Card>

            <Card className="border-destructive/30 ring-destructive/20">
              <CardHeader className="border-b border-destructive/20">
                <CardTitle className="flex items-center gap-2 text-destructive">
                  <Trash2 className="size-4"/> Destructive action
                </CardTitle>
              </CardHeader>
              <CardContent>
                {!confirmDelete ? (
                  <Button className="w-full" variant="destructive" onClick={() => setConfirmDelete(true)}>
                    Delete permanently
                  </Button>
                ) : (
                  <div className="space-y-3">
                    <p className="text-destructive">This cannot be undone.</p>
                    <div className="flex gap-2">
                      <Button className="flex-1" variant="outline" onClick={() => setConfirmDelete(false)}>Cancel</Button>
                      <Button className="flex-1" variant="destructive" onClick={deleteFile}>Confirm</Button>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          </aside>
        </div>
      </div>
    </main>
  )
}

function Metric({icon: Icon, label, value}: {
  icon: typeof Fingerprint
  label: string
  value: string
}) {
  return (
    <div className="bg-card p-4">
      <div className="mb-3 flex items-center gap-2 text-muted-foreground"><Icon className="size-4"/>{label}</div>
      <p className="text-lg font-medium">{value}</p>
    </div>
  )
}

function Detail({label, value, mono = false}: {label: string; value: string; mono?: boolean}) {
  return (
    <div>
      <p className="mb-1 text-muted-foreground">{label}</p>
      <p className={`break-all ${mono ? 'font-mono text-[0.6875rem]' : ''}`}>{value}</p>
    </div>
  )
}
