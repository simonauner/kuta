import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

// The platform's own checkbox and radio in the app's accent colour; pair with FormField layout="choice".
// className is for layout only, such as mt-1 to line up with the first line of a long label.
export function Checkbox({ className, ...props }: Omit<ComponentProps<'input'>, 'type'>) {
  return (
    <input type="checkbox" className={cn('size-4 shrink-0 accent-primary', className)} {...props} />
  )
}

export function Radio({ className, ...props }: Omit<ComponentProps<'input'>, 'type'>) {
  return (
    <input type="radio" className={cn('size-4 shrink-0 accent-primary', className)} {...props} />
  )
}
