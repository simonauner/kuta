import type { ComponentProps } from 'react'
import type { VariantProps } from 'class-variance-authority'
import { card } from '@/components/ui/card-variants'

export function Card({
  tone,
  padding,
  interactive,
  dashed,
  className,
  ...props
}: ComponentProps<'div'> & VariantProps<typeof card>) {
  return <div {...props} className={card({ tone, padding, interactive, dashed, className })} />
}
