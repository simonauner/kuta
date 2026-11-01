import { useId, type ReactNode } from 'react'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

export function FormField({
  label,
  action,
  layout = 'stacked',
  hideLabel = false,
  labelClassName,
  className,
  children,
}: {
  label: ReactNode
  action?: ReactNode
  layout?: 'stacked' | 'inline' | 'choice'
  hideLabel?: boolean
  labelClassName?: string
  className?: string
  children: (id: string) => ReactNode
}) {
  const id = useId()
  if (layout === 'choice') {
    return (
      <Label
        htmlFor={id}
        className={cn('cursor-pointer items-center gap-2 font-normal leading-normal', className)}
      >
        {children(id)}
        <span className={labelClassName}>{label}</span>
      </Label>
    )
  }
  return (
    <div className={cn(layout === 'inline' && 'flex flex-wrap items-center gap-2', className)}>
      <div
        className={cn(
          'flex min-h-4 items-center justify-between gap-2',
          layout === 'stacked' && 'mb-2',
          hideLabel && 'sr-only',
        )}
      >
        <Label htmlFor={id} className={labelClassName}>
          {label}
        </Label>
        {action}
      </div>
      {children(id)}
    </div>
  )
}
