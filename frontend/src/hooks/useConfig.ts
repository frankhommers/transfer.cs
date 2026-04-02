import {useEffect, useState} from 'react'

export interface AppConfig {
    title: string
    purgeDays: number
    maxUploadSizeKb: number
}

const defaultConfig: AppConfig = {
    title: 'transfer.cs',
    purgeDays: 0,
    maxUploadSizeKb: 0,
}

export function useConfig() {
    const [config, setConfig] = useState<AppConfig>(defaultConfig)

    useEffect(() => {
        fetch('/api/config')
            .then((res) => res.json())
            .then((data) => {
                const merged = {...defaultConfig, ...data}
                setConfig(merged)
                document.title = merged.title
            })
            .catch(() => {
            })
    }, [])

    return config
}
