import {createContext} from 'react'

export interface AppConfig {
    title: string
    purgeDays: number
    maxUploadSizeKb: number
}

export const defaultConfig: AppConfig = {
    title: document.title || 'transfer.cs',
    purgeDays: 0,
    maxUploadSizeKb: 0,
}

export const ConfigContext = createContext<AppConfig>(defaultConfig)
