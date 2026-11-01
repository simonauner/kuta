import { useId, useRef, useState } from 'react'
import { Upload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Heading } from '@/components/ui/heading'

// Drop or choose one or more FIT files from the watch.
export function FitDrop({
  onFiles,
  disabled,
}: {
  onFiles: (files: File[]) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)
  const helpId = useId()
  return (
    <Card
      dashed
      tone={dragging ? 'highlight' : 'default'}
      padding="spacious"
      className="text-center"
      onDragOver={(event) => {
        event.preventDefault()
        setDragging(true)
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(event) => {
        event.preventDefault()
        setDragging(false)
        const files = [...event.dataTransfer.files].filter((file) =>
          file.name.toLowerCase().endsWith('.fit'),
        )
        if (!disabled && files.length > 0) onFiles(files)
      }}
    >
      <Upload aria-hidden="true" className="mx-auto mb-3 size-6 text-primary" />
      <Heading level={2}>{t('newActivity.dropTitle')}</Heading>
      <p id={helpId} className="mx-auto mt-2 max-w-md text-sm text-muted-foreground">
        {t('newActivity.dropHelp')}
      </p>
      <Button
        type="button"
        className="mt-5"
        disabled={disabled}
        aria-describedby={helpId}
        onClick={() => input.current?.click()}
      >
        {t('newActivity.chooseFiles')}
      </Button>
      <input
        ref={input}
        type="file"
        accept=".fit"
        multiple
        hidden
        aria-label={t('newActivity.file')}
        onChange={(event) => {
          const files = [...(event.currentTarget.files ?? [])]
          event.currentTarget.value = '' // the same file can be chosen again
          if (files.length > 0) onFiles(files)
        }}
      />
    </Card>
  )
}
