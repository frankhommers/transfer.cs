import {useContext} from 'react'
import {ConfigContext} from '@/lib/config'

export function useConfig() {
    return useContext(ConfigContext)
}
