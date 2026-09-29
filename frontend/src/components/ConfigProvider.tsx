import {useEffect, useState, type ReactNode} from 'react'
import {ConfigContext, defaultConfig, type AppConfig} from '@/lib/config'

export function ConfigProvider({children}: { children: ReactNode }) {
    const [config, setConfig] = useState<AppConfig>(defaultConfig)

    useEffect(() => {
        fetch('/api/config')
            .then((res) => res.json())
            .then((data) => setConfig({...defaultConfig, ...data}))
            .catch(() => {
            })
    }, [])

    return <ConfigContext.Provider value={config}>{children}</ConfigContext.Provider>
}
