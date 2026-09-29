import {Link} from 'react-router-dom'
import {useConfig} from '@/hooks/useConfig'
import {cn} from '@/lib/utils'

export function SiteBrandLink({className}: { className?: string }) {
    const {title} = useConfig()

    return (
        <Link
            to="/"
            className={cn(
                'text-sm font-semibold tracking-tight text-muted-foreground hover:text-foreground transition-colors',
                className,
            )}
        >
            {title}
        </Link>
    )
}
