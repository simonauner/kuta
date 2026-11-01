import type { ReactNode } from 'react'
import { LoaderCircle } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'

// Covers its content with a spinner while loading. The overlay fades in after 0.2 s, so fast loads don't flash.
export function Loader({
  loading,
  className,
  children,
}: {
  loading: boolean
  className?: string
  children: ReactNode
}) {
  const { t } = useTranslation()
  return (
    <div className={cn('relative', className)} aria-busy={loading}>
      {children}
      <div
        aria-hidden={!loading}
        className={cn(
          'absolute inset-0 grid place-items-center rounded-[inherit] py-6 bg-background/60 transition-opacity duration-200',
          loading ? 'opacity-100 delay-200' : 'pointer-events-none opacity-0',
        )}
      >
        {/* Sticky so the spinner stays on screen when the panel is taller than the viewport. */}
        <LoaderCircle className="sticky top-[calc(50vh-1rem)] bottom-[calc(50vh-1rem)] size-8 text-foreground motion-safe:animate-spin" />
      </div>
      <span role="status" className="sr-only">
        {loading && t('loading')}
      </span>
    </div>
  )
}
