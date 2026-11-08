import { useTranslation } from 'react-i18next'
import { ApiError } from '@/lib/api'
import { cn } from '@/lib/utils'

export function ErrorMessage({ error }: { error: Error }) {
  const { t } = useTranslation()
  const key = error instanceof ApiError ? `errors.${error.code}` : 'errors.network'
  return t(key as 'errors.generic', { defaultValue: t('errors.generic') })
}

// An error announced as an alert, or nothing without an error. className is for layout only.
export function ErrorText({
  error,
  className,
}: {
  error: Error | null | undefined
  className?: string
}) {
  if (!error) return null
  return (
    <p role="alert" className={cn('text-sm text-destructive', className)}>
      <ErrorMessage error={error} />
    </p>
  )
}
