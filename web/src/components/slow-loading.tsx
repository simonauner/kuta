import { useEffect, useState, type ReactNode } from 'react'
import { LoaderCircle } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { scheduleSlowLoading } from '@/lib/slow-loading'

export function SlowLoading({ loading, children }: { loading: boolean; children: ReactNode }) {
  const { t } = useTranslation()
  const [shownAt, setShownAt] = useState<number | null>(null)

  useEffect(() => scheduleSlowLoading(loading, shownAt, setShownAt), [loading, shownAt])

  if (shownAt !== null)
    return (
      <p role="status" className="mt-8 flex items-center gap-2 text-muted-foreground">
        <LoaderCircle aria-hidden="true" className="size-4 motion-safe:animate-spin" />
        {t('slowLoading')}
      </p>
    )

  return loading ? null : children
}
