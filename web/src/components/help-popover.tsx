import type { ReactNode } from 'react'
import { CircleQuestionMark } from 'lucide-react'
import { Popover } from 'radix-ui'
import { Button } from '@/components/ui/button'

export function HelpPopover({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Popover.Root>
      <Popover.Trigger asChild>
        <Button type="button" variant="ghost" size="icon" aria-label={label}>
          <CircleQuestionMark aria-hidden="true" className="size-5 text-muted-foreground" />
        </Button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          aria-label={label}
          align="end"
          sideOffset={8}
          collisionPadding={16}
          className="z-50 max-h-[min(28rem,var(--radix-popover-content-available-height))] w-80 max-w-[calc(100vw-2rem)] overflow-y-auto rounded-md border bg-background p-4 text-sm leading-relaxed text-foreground shadow-md outline-none"
        >
          {children}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}
