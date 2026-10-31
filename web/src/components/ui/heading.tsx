import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

const headingStyles = {
  1: 'text-page font-semibold tracking-tight',
  2: 'text-section font-semibold tracking-tight',
  3: 'text-subsection font-semibold',
} as const

export function Heading({
  level,
  className,
  ...props
}: ComponentProps<'h1'> & { level: 1 | 2 | 3 }) {
  const Tag = `h${level}` as const
  return <Tag {...props} className={cn(headingStyles[level], className)} />
}
