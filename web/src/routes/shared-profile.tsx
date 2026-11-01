import { useQuery } from '@tanstack/react-query'
import { Navigate, useParams } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { ErrorMessage } from '@/components/error-message'
import { SlowLoading } from '@/components/slow-loading'
import { ApiError, api } from '@/lib/api'
import { meQuery } from '@/lib/auth'

// A short profile link (/app/r/$code) only finds the runner; the runner page decides what may be shown.
export function SharedProfilePage() {
  const { code } = useParams({ from: '/app/r/$code' })
  const { t } = useTranslation()
  // Without an account the code is looked up through the public API
  const visitor = useQuery(meQuery).data === null
  const runner = useQuery({
    queryKey: ['share-code', code, visitor],
    queryFn: () =>
      api.get<{ runnerId: string }>(
        `/api/${visitor ? 'public/' : ''}share-codes/${encodeURIComponent(code)}`,
      ),
    retry: false,
  })
  if (runner.data)
    return (
      <Navigate to="/app/runners/$runnerId" params={{ runnerId: runner.data.runnerId }} replace />
    )
  return (
    <SlowLoading loading={runner.isPending}>
      {runner.error && (
        <p role="alert">
          {runner.error instanceof ApiError && runner.error.status === 404 ? (
            t('social.profileLinkInvalid')
          ) : (
            <ErrorMessage error={runner.error} />
          )}
        </p>
      )}
    </SlowLoading>
  )
}
