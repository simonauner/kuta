import { useEffect, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ErrorText } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { Heading } from '@/components/ui/heading'
import { Modal } from '@/components/ui/modal'

// Asks before an action that cannot be undone. Open while `open` is true; Escape, outside click and Cancel call onClose
// unless the action is pending. children are extra fields (such as a password) that Enter submits; confirmDisabled
// keeps the action off until they are filled in. confirmVariant is primary-filled for a confirmed action that does not
// destroy data.
export function ConfirmationModal({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel,
  confirmVariant = 'destructive-outline',
  confirmDisabled = false,
  pending = false,
  error,
  onConfirm,
  onClose,
  children,
}: {
  open: boolean
  title: ReactNode
  description: ReactNode
  confirmLabel: string
  cancelLabel: string
  confirmVariant?: 'destructive-outline' | 'primary-filled'
  confirmDisabled?: boolean
  pending?: boolean
  error?: Error | null
  onConfirm: () => void
  onClose: () => void
  children?: ReactNode
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const descriptionId = useId()

  useEffect(() => {
    if (open) dialog.current?.showModal()
    else dialog.current?.close()
  }, [open])

  // A portal, so the form is not nested in a form that holds the modal.
  return createPortal(
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
      <form
        onSubmit={(event) => {
          event.preventDefault()
          // React events bubble through the portal to the surrounding form.
          event.stopPropagation()
          if (!pending && !confirmDisabled) onConfirm()
        }}
      >
        {children && <div className="mt-5">{children}</div>}
        <ErrorText error={error} className="mt-3" />
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <Button type="button" variant="secondary-outline" disabled={pending} onClick={onClose}>
            {cancelLabel}
          </Button>
          <Button type="submit" variant={confirmVariant} disabled={pending || confirmDisabled}>
            {confirmLabel}
          </Button>
        </div>
      </form>
    </Modal>,
    document.body,
  )
}
