import {type FormEvent, useCallback, useEffect, useState} from 'react'
import {useParams} from 'react-router-dom'
import {Download, QrCode, FileIcon, Lock, Eye, EyeOff, Loader2} from 'lucide-react'
import {Button, buttonVariants} from '@/components/ui/button'
import {Card, CardContent, CardHeader, CardTitle} from '@/components/ui/card'
import {Badge} from '@/components/ui/badge'
import {Input} from '@/components/ui/input'
import {cn} from '@/lib/utils'
import {SiteBrandLink} from '@/components/SiteBrandLink'
import {useConfig} from '@/hooks/useConfig'
import {useDocumentTitle} from '@/hooks/useDocumentTitle'

interface LockedPreview {
    filename: string
    url: string
    downloadUrl: string
    token: string
    hostname: string
    qrCode: string
    passwordProtected: boolean
}

interface PreviewData extends LockedPreview {
    contentType: string
    contentLength: number
    previewType: 'image' | 'video' | 'audio' | 'markdown' | 'text' | 'generic'
}

function isUnlocked(preview: LockedPreview): preview is PreviewData {
    return 'previewType' in preview
}

function formatRetryAfter(header: string | null): string {
    const seconds = Number(header)
    if (!Number.isFinite(seconds) || seconds <= 0) return 'later'
    if (seconds < 60) return `in ${Math.ceil(seconds)} second${seconds <= 1 ? '' : 's'}`
    const minutes = Math.ceil(seconds / 60)
    return `in ${minutes} minute${minutes === 1 ? '' : 's'}`
}

function formatBytes(bytes: number): string {
    if (bytes === 0) return '0 Bytes'
    const k = 1024
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i]
}

export function PreviewPage() {
    const {token, filename} = useParams<{ token: string; filename: string }>()
    const [preview, setPreview] = useState<LockedPreview | null>(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState<string | null>(null)
    const [showQr, setShowQr] = useState(false)
    const [textContent, setTextContent] = useState<string | null>(null)
    const {title} = useConfig()
    useDocumentTitle(`${preview?.filename ?? filename} · ${title}`)

    const loadPreview = useCallback(async () => {
        const res = await fetch(`/api/preview/${token}/${filename}`, {cache: 'no-store'})
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const data: LockedPreview = await res.json()
        setPreview(data)

        if (isUnlocked(data) && (data.previewType === 'text' || data.previewType === 'markdown')) {
            const textRes = await fetch(`/inline/${token}/${filename}`)
            if (textRes.ok) {
                setTextContent(await textRes.text())
            }
        }
    }, [token, filename])

    useEffect(() => {
        async function fetchPreview() {
            try {
                await loadPreview()
            } catch (e) {
                setError(e instanceof Error ? e.message : 'Failed to load preview')
            } finally {
                setLoading(false)
            }
        }

        fetchPreview()
    }, [loadPreview])

    if (loading) {
        return (
            <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
                <SiteBrandLink/>
                <p className="text-muted-foreground">Loading...</p>
            </div>
        )
    }

    if (error || !preview) {
        return (
            <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
                <SiteBrandLink/>
                <p className="text-destructive">{error || 'File not found'}</p>
            </div>
        )
    }

    if (!isUnlocked(preview)) {
        return (
            <div className="min-h-screen bg-background">
                <div className="max-w-md mx-auto px-4 py-16">
                    <SiteBrandLink className="mb-6 inline-block"/>
                    <Card>
                        <CardHeader>
                            <div className="flex items-center gap-2 text-muted-foreground">
                                <Lock className="h-4 w-4"/>
                                <span className="text-sm">Password protected</span>
                            </div>
                            <CardTitle className="text-2xl break-all">{preview.filename}</CardTitle>
                        </CardHeader>
                        <CardContent>
                            <UnlockForm token={token ?? ''} filename={filename ?? ''} onUnlocked={loadPreview}/>
                        </CardContent>
                    </Card>
                </div>
            </div>
        )
    }

    const inlineUrl = `/inline/${token}/${filename}`

    return (
        <div className="min-h-screen bg-background">
            <div className="max-w-4xl mx-auto px-4 py-8">
                <SiteBrandLink className="mb-6 inline-block"/>
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
                                    {preview.passwordProtected && (
                                        <Badge variant="outline">
                                            <Lock/> Password protected
                                        </Badge>
                                    )}
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
                                        <QrCode className="h-4 w-4"/>
                                    </Button>
                                )}
                                <a
                                    href={preview.downloadUrl}
                                    className={cn(buttonVariants({variant: 'default'}))}
                                >
                                    <Download className="h-4 w-4 mr-2"/>
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

function UnlockForm({
                        token,
                        filename,
                        onUnlocked,
                    }: {
    token: string
    filename: string
    onUnlocked: () => Promise<void>
}) {
    const [password, setPassword] = useState('')
    const [showPassword, setShowPassword] = useState(false)
    const [submitting, setSubmitting] = useState(false)
    const [message, setMessage] = useState<string | null>(null)

    const submit = async (e: FormEvent) => {
        e.preventDefault()
        if (!password || submitting) return
        setSubmitting(true)
        setMessage(null)
        try {
            const res = await fetch(`/api/unlock/${encodeURIComponent(token)}/${encodeURIComponent(filename)}`, {
                method: 'POST',
                headers: {'Content-Type': 'application/json'},
                body: JSON.stringify({password}),
                cache: 'no-store',
            })
            if (res.ok) {
                await onUnlocked()
            } else if (res.status === 401) {
                setMessage('Incorrect password. Please try again.')
            } else if (res.status === 429) {
                setMessage(`Too many incorrect attempts. Try again ${formatRetryAfter(res.headers.get('Retry-After'))}.`)
            } else if (res.status === 404) {
                setMessage('This file no longer exists.')
            } else {
                setMessage(`Unlock failed (HTTP ${res.status}).`)
            }
        } catch {
            setMessage('Network error. Please try again.')
        } finally {
            setSubmitting(false)
        }
    }

    return (
        <form onSubmit={submit} className="space-y-3">
            <p className="text-sm text-muted-foreground">Enter the password you received to view and download this file.</p>
            <div className="flex items-center gap-2">
                <Input
                    type={showPassword ? 'text' : 'password'}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="Password"
                    aria-label="Password"
                    aria-invalid={message !== null}
                    autoComplete="current-password"
                    autoFocus
                />
                <Button
                    type="button"
                    variant="outline"
                    size="icon"
                    onClick={() => setShowPassword((prev) => !prev)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    title={showPassword ? 'Hide password' : 'Show password'}
                >
                    {showPassword ? <EyeOff className="h-4 w-4"/> : <Eye className="h-4 w-4"/>}
                </Button>
            </div>
            {message && <p className="text-sm text-destructive" role="alert">{message}</p>}
            <Button type="submit" className="w-full" disabled={!password || submitting}>
                {submitting ? <Loader2 className="h-4 w-4 mr-2 animate-spin"/> : <Lock className="h-4 w-4 mr-2"/>}
                Unlock
            </Button>
        </form>
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
                <audio src={inlineUrl} controls className="w-full"/>
            )
        case 'text':
        case 'markdown':
            return (
                <pre
                    className="bg-muted rounded-lg p-4 overflow-x-auto text-sm font-mono whitespace-pre-wrap break-words max-h-[70vh] overflow-y-auto">
          {textContent ?? 'Loading...'}
        </pre>
            )
        default:
            return (
                <div className="flex flex-col items-center justify-center py-12 text-muted-foreground">
                    <FileIcon className="h-16 w-16 mb-4"/>
                    <p>No preview available</p>
                </div>
            )
    }
}
