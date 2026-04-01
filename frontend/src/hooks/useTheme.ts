import {useEffect, useState} from 'react'

export type ThemeMode = 'system' | 'light' | 'dark'

function resolveEffective(mode: ThemeMode): 'light' | 'dark' {
    if (mode === 'system') {
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
    }
    return mode
}

export function useTheme() {
    const [mode, setMode] = useState<ThemeMode>(() => {
        const stored = window.localStorage.getItem('transfer-cs-theme')
        if (stored === 'dark' || stored === 'light' || stored === 'system') return stored
        return 'system'
    })

    useEffect(() => {
        const apply = () => {
            document.documentElement.dataset.theme = resolveEffective(mode)
        }

        apply()
        window.localStorage.setItem('transfer-cs-theme', mode)

        if (mode === 'system') {
            const mq = window.matchMedia('(prefers-color-scheme: dark)')
            mq.addEventListener('change', apply)
            return () => mq.removeEventListener('change', apply)
        }
    }, [mode])

    const effectiveMode = resolveEffective(mode)

    return {mode, setMode, effectiveMode} as const
}
