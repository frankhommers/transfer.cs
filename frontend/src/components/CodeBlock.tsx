import {useState} from 'react'
import {Copy, Check} from 'lucide-react'

interface CodeBlockProps {
    code: string
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

export function CodeBlock({code}: CodeBlockProps) {
    const [copied, setCopied] = useState(false)

    const handleCopy = async () => {
        await copyToClipboard(code)
        setCopied(true)
        setTimeout(() => setCopied(false), 2000)
    }

    return (
        <div
            className="group relative flex items-center bg-muted border border-border rounded-md cursor-pointer hover:border-primary/30 transition-colors"
            onClick={handleCopy}
        >
            <code className="flex-1 px-4 py-3 text-sm font-mono text-foreground overflow-x-auto whitespace-pre">
                {code}
            </code>
            <button
                type="button"
                className="shrink-0 p-3 text-muted-foreground hover:text-foreground transition-colors border-l border-border"
                aria-label="Copy to clipboard"
            >
                {copied ? (
                    <Check className="h-4 w-4 text-green-500"/>
                ) : (
                    <Copy className="h-4 w-4"/>
                )}
            </button>
        </div>
    )
}
