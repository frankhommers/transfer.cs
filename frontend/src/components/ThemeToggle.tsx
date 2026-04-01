import {Moon, Sun, Monitor} from 'lucide-react'
import type {ThemeMode} from '../hooks/useTheme'

const modes = [
    {value: 'system', Icon: Monitor},
    {value: 'light', Icon: Sun},
    {value: 'dark', Icon: Moon},
] as const

type ThemeToggleProps = {
    mode: ThemeMode
    onChange: (mode: ThemeMode) => void
}

export function ThemeToggle({mode, onChange}: ThemeToggleProps) {
    const activeIndex = modes.findIndex((m) => m.value === mode)
    const offset = 4 + activeIndex * 30

    return (
        <div
            className="relative inline-flex h-9 items-center rounded-full border border-border p-1 gap-0.5 bg-card/75 backdrop-blur-md"
        >
            <div
                className="absolute h-7 w-7 rounded-full bg-primary shadow-sm transition-all duration-300 ease-in-out"
                style={{left: `${offset}px`}}
            />

            {modes.map(({value, Icon}) => (
                <button
                    key={value}
                    type="button"
                    aria-label={value}
                    aria-pressed={mode === value}
                    onClick={() => onChange(value)}
                    className={[
                        'relative z-10 inline-flex h-7 w-7 items-center justify-center rounded-full transition-colors duration-300',
                        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                        mode === value
                            ? 'text-primary-foreground'
                            : 'text-muted-foreground hover:text-foreground',
                    ].join(' ')}
                >
                    <Icon className="h-3.5 w-3.5"/>
                </button>
            ))}
        </div>
    )
}
