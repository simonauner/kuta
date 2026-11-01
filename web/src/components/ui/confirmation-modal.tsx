import { useEffect, useId, useRef, type ReactNode } from 'react'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Heading } from '@/components/ui/heading'
import { Modal } from '@/components/ui/modal'

// Asks before an action that cannot be undone. Open while `open` is true; Escape, outside click and Cancel call onClose
// unless the action is pending.
export function ConfirmationModal({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel,
  pending = false,
  error,
  onConfirm,
  onClose,
}: {
  open: boolean
  title: ReactNode
  description: ReactNode
  confirmLabel: string
  cancelLabel: string
  pending?: boolean
  error?: Error | null
  onConfirm: () => void
  onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const descriptionId = useId()

  useEffect(() => {
    if (open) dialog.current?.showModal()
    else dialog.current?.close()
  }, [open])

  return (
    <Modal
      ref={dialog}
      aria-labelledby={titleId}
      aria-describedby={descriptionId}
      onCancel={(event) => {
        event.preventDefault()
        if (!pending) onClose()
      }}
    >
      <Heading level={2} id={titleId} className="break-words">
        {title}
      </Heading>
      <p id={descriptionId} className="mt-3 text-sm leading-relaxed text-muted-foreground">
        {description}
      </p>
      {error && (
        <p role="alert" className="mt-3 text-sm text-destructive">
          <ErrorMessage error={error} />
        </p>
      )}
      <div className="mt-6 flex flex-wrap justify-end gap-3">
        <Button type="button" variant="secondary-outline" disabled={pending} onClick={onClose}>
          {cancelLabel}
        </Button>
        <Button type="button" variant="destructive-outline" disabled={pending} onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </Modal>
  )
}
