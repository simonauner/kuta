import type { ReactNode } from 'react'
import { Maximize2, Minimize2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { card } from '@/components/ui/card-variants'
import { cn } from '@/lib/utils'

// A card that a large screen can widen beyond the column, like the enlarged map: centred and kept inside the
// viewport. className is for layout only.
export function ExpandableCard({
  large,
  className,
  children,
}: {
  large: boolean
  className?: string
  children: ReactNode
}) {
  return (
    <div
      className={card({
        className: cn(
          'transition-[margin] duration-300 motion-reduce:transition-none',
          large && 'lg:mx-[calc(50%-min(50vw-1rem,40rem))]',
          className,
        ),
      })}
    >
      {children}
    </div>
  )
}

// Toggles ExpandableCard. Hidden on a small screen, where the chart already fills the width.
export function ExpandToggle({
  large,
  onChange,
  className,
}: {
  large: boolean
  onChange: (large: boolean) => void
  className?: string
}) {
  const { t } = useTranslation()
  const label = t(large ? 'stats.chartSmaller' : 'stats.chartLarger')
  return (
    <Button
      type="button"
      variant="secondary-outline"
      size="icon"
      className={cn('hidden lg:inline-flex', className)}
      aria-label={label}
      title={label}
      aria-pressed={large}
      onClick={() => onChange(!large)}
    >
      {large ? <Minimize2 /> : <Maximize2 />}
    </Button>
  )
}
