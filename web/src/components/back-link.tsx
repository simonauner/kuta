import { Link, useRouter, useSearch } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'

const linkClass = 'text-sm text-muted-foreground underline underline-offset-2'

// Back to where the activity was opened from (returnTo), otherwise to the activity list or the feed.
export function BackLink({ fallback = 'activities' }: { fallback?: 'activities' | 'feed' }) {
  const { t } = useTranslation()
  const router = useRouter()
  const { year, tag, sport, returnTo } = useSearch({ strict: false })
  if (returnTo)
    return (
      <a
        href={returnTo}
        className={linkClass}
        onClick={(event) => {
          if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey) return
          event.preventDefault()
          router.history.push(returnTo)
        }}
      >
        ← {t(backLabel(returnTo))}
      </a>
    )
  if (fallback === 'feed')
    return (
      <Link to="/app" className={linkClass}>
        ← {t('nav.feed')}
      </Link>
    )
  return (
    <Link to="/app/activities" search={{ year, tag, sport }} className={linkClass}>
      ← {t('activity.back')}
    </Link>
  )
}

function backLabel(path: string) {
  const pathname = path.split(/[?#]/)[0].replace(/\/$/, '')
  if (pathname === '/app') return 'nav.feed'
  if (pathname === '/app/activities') return 'activity.back'
  if (pathname === '/app/stats') return 'nav.stats'
  if (pathname === '/app/profile') return 'nav.profile'
  if (pathname.startsWith('/app/runners/')) return 'activity.backToRunner'
  return 'common.back'
}
