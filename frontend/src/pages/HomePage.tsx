import {useState} from 'react'
import {Icon} from '@mdi/react'
import {mdiRobotHappy, mdiContentCopy, mdiCheck, mdiGithub, mdiConsole, mdiChevronDown} from '@mdi/js'
import {UploadDropzone} from '@/components/UploadDropzone'
import {CommandComposer} from '@/components/CommandComposer'
import {ExampleSnippets} from '@/components/ExampleSnippets'
import {Separator} from '@/components/ui/separator'
import {useConfig} from '@/hooks/useConfig'

export function HomePage() {
    const baseUrl = window.location.origin
    const config = useConfig()
    const [copied, setCopied] = useState(false)
    const [cliOpen, setCliOpen] = useState(false)
    const skillUrl = `${baseUrl}/SKILL.md`

    const copySkillUrl = async () => {
        await navigator.clipboard.writeText(skillUrl)
        setCopied(true)
        setTimeout(() => setCopied(false), 2000)
    }

    return (
        <div className="min-h-screen bg-background">
            <div className="max-w-3xl mx-auto px-4 py-16">
                <div className="text-center mb-12">
                    <h1 className="text-4xl font-bold tracking-tight mb-2">{config.title}</h1>
                    <p className="text-lg text-muted-foreground">
                        Easy and fast file sharing
                    </p>
                </div>

                <UploadDropzone/>

                <Separator className="my-12"/>

                <button
                    type="button"
                    onClick={() => setCliOpen((prev) => !prev)}
                    className="flex items-center gap-2 w-full text-left group"
                >
                    <Icon path={mdiConsole} size={0.75} className="text-muted-foreground shrink-0"/>
                    <span className="text-sm font-medium text-muted-foreground group-hover:text-foreground transition-colors">Command Line Usage</span>
                    <Icon
                        path={mdiChevronDown}
                        size={0.625}
                        className={[
                            'text-muted-foreground transition-transform ml-auto',
                            cliOpen ? 'rotate-180' : '',
                        ].join(' ')}
                    />
                </button>

                {cliOpen && (
                    <div className="mt-6">
                        <CommandComposer baseUrl={baseUrl}/>
                    </div>
                )}

                <Separator className="my-12"/>

                <ExampleSnippets baseUrl={baseUrl}/>

                <Separator className="my-12"/>

                <div className="flex items-center justify-between">
                    <p className="flex items-center gap-2 text-sm text-muted-foreground">
                        <Icon path={mdiRobotHappy} size={0.75}/>
                        AI agent? Download the{' '}
                        <a
                            href={skillUrl}
                            className="underline hover:text-foreground transition-colors"
                        >SKILL.md</a>{' '}
                        for this instance
                        <button
                            type="button"
                            onClick={copySkillUrl}
                            className="text-muted-foreground hover:text-foreground transition-colors"
                            aria-label="Copy SKILL.md URL"
                        >
                            <Icon path={copied ? mdiCheck : mdiContentCopy} size={0.625}/>
                        </button>
                    </p>
                    <a
                        href="https://github.com/frankhommers/transfer.cs"
                        className="text-muted-foreground hover:text-foreground transition-colors"
                        aria-label="GitHub"
                    >
                        <Icon path={mdiGithub} size={1}/>
                    </a>
                </div>
            </div>
        </div>
    )
}
