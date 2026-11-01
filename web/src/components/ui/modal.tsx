import { useRef, type ComponentProps, type PointerEvent } from 'react'
import { cn } from '@/lib/utils'

function isOutside(event: PointerEvent<HTMLDialogElement>) {
  const rect = event.currentTarget.getBoundingClientRect()
  return (
    event.target === event.currentTarget &&
    (event.clientX < rect.left ||
      event.clientX > rect.right ||
      event.clientY < rect.top ||
      event.clientY > rect.bottom)
  )
}

export function Modal({
  closeOnOutsideClick = true,
  className,
  onPointerDown,
  onPointerUp,
  ...props
}: ComponentProps<'dialog'> & { closeOnOutsideClick?: boolean }) {
  const startedOutside = useRef(false)
  return (
    <dialog
      {...props}
      className={cn(
        'fixed inset-0 m-auto max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-md overflow-y-auto rounded-xl border bg-background p-6 text-foreground shadow-xl backdrop:bg-black/70',
        className,
      )}
      onPointerDown={(event) => {
        onPointerDown?.(event)
        startedOutside.current =
          closeOnOutsideClick && !event.defaultPrevented && event.button === 0 && isOutside(event)
      }}
      onPointerUp={(event) => {
        onPointerUp?.(event)
        const dismiss =
          startedOutside.current &&
          closeOnOutsideClick &&
          !event.defaultPrevented &&
          isOutside(event)
        startedOutside.current = false
        if (!dismiss) return
        const dialog = event.currentTarget
        // Same cancelable event as Escape, so controlled modals also follow their onCancel rules.
        if (dialog.dispatchEvent(new Event('cancel', { cancelable: true }))) dialog.close()
      }}
    />
  )
}
