import type { ReactNode } from 'react'
import { CircleQuestionMark } from 'lucide-react'
import { Popover } from '@base-ui/react/popover'
import { Button } from '@/components/ui/button'

export function HelpPopover({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Popover.Root>
      <Popover.Trigger
        render={<Button type="button" variant="ghost" size="icon" aria-label={label} />}
      >
        <CircleQuestionMark aria-hidden="true" className="size-5 text-muted-foreground" />
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Positioner align="end" sideOffset={8} collisionPadding={16} className="z-50">
          <Popover.Popup
            aria-label={label}
            className="max-h-[min(28rem,var(--available-height))] w-80 max-w-[calc(100vw-2rem)] overflow-y-auto rounded-md border bg-background p-4 text-sm leading-relaxed text-foreground shadow-md outline-none"
          >
            {children}
          </Popover.Popup>
        </Popover.Positioner>
      </Popover.Portal>
    </Popover.Root>
  )
}
