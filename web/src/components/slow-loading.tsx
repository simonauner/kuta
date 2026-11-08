import { useEffect, useState, type ReactNode } from 'react'
import { LoaderCircle } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { scheduleSlowLoading } from '@/lib/slow-loading'

function useSlow(loading: boolean) {
  const [shownAt, setShownAt] = useState<number | null>(null)
  useEffect(() => scheduleSlowLoading(loading, shownAt, setShownAt), [loading, shownAt])
  return shownAt !== null
}

function SlowMessage() {
  const { t } = useTranslation()
  return (
    <>
      <LoaderCircle aria-hidden="true" className="size-4 motion-safe:animate-spin" />
      {t('slowLoading')}
    </>
  )
}

// Shows nothing while loading, and a message once loading is slow.
export function SlowLoading({ loading, children }: { loading: boolean; children: ReactNode }) {
  if (useSlow(loading))
    return (
      <p role="status" className="mt-8 flex items-center gap-2 text-muted-foreground">
        <SlowMessage />
      </p>
    )

  return loading ? null : children
}

// Keeps the previous content while new data loads; once loading is slow it is dimmed under the same message.
export function SlowRefresh({ loading, children }: { loading: boolean; children: ReactNode }) {
  const slow = useSlow(loading)
  return (
    <div className="relative" aria-busy={loading}>
      {slow && (
        <p role="status" className="absolute inset-x-0 top-8 z-10 flex justify-center">
          <span className="flex items-center gap-2 rounded-full border bg-background px-3 py-1.5 text-sm text-muted-foreground shadow-sm">
            <SlowMessage />
          </span>
        </p>
      )}
      <div className={slow ? 'opacity-50 transition-opacity' : 'transition-opacity'}>
        {children}
      </div>
    </div>
  )
}
