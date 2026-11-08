import type { ReactNode } from 'react'
import { card } from '@/components/ui/card-variants'
import { cn } from '@/lib/utils'

const valueSizes = {
  sm: 'text-sm font-medium',
  md: 'font-semibold',
  lg: 'text-lg font-semibold',
  xl: 'text-xl font-semibold',
}

// One label and value in a <dl>. boxed puts it on its own compact card with a larger value; children are extra
// <dd> rows, usually StatHint. className is for layout only.
export function Stat({
  label,
  value,
  size = 'md',
  boxed = false,
  className,
  children,
}: {
  label: ReactNode
  value: ReactNode
  size?: keyof typeof valueSizes
  boxed?: boolean
  className?: string
  children?: ReactNode
}) {
  return (
    <div className={boxed ? card({ padding: 'compact', className }) : className}>
      <dt className="text-metadata text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          'tabular-nums',
          boxed ? 'mt-2 break-words text-lg font-semibold sm:text-2xl' : valueSizes[size],
        )}
      >
        {value}
      </dd>
      {children}
    </div>
  )
}

export function StatHint({ children }: { children: ReactNode }) {
  return <dd className="text-metadata text-muted-foreground tabular-nums">{children}</dd>
}
