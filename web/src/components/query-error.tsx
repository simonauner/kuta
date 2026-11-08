import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

// A failed query with a retry button. Renders nothing while the query has no error; className is for layout only.
export function QueryError({
  query,
  className,
}: {
  query: { error: Error | null; isFetching: boolean; refetch: () => unknown }
  className?: string
}) {
  const { t } = useTranslation()
  if (!query.error) return null
  return (
    <p role="alert" className={cn('text-sm text-destructive', className)}>
      <ErrorMessage error={query.error} />{' '}
      <Button
        variant="secondary-outline"
        size="sm"
        disabled={query.isFetching}
        onClick={() => void query.refetch()}
      >
        {t('activities.retry')}
      </Button>
    </p>
  )
}
