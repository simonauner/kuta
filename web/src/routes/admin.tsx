import { queryOptions, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { QueryError } from '@/components/query-error'
import { SlowLoading } from '@/components/slow-loading'
import { Stat } from '@/components/stat'
import { Heading } from '@/components/ui/heading'
import { api, ApiError } from '@/lib/api'

type AdminStats = {
  accounts: number
  confirmedAccounts: number
  newAccounts30Days: number
  signedIn30Days: number
  accountsWithActivities: number
  activities: number
  newActivities30Days: number
}

const adminStatsQuery = queryOptions({
  queryKey: ['admin', 'stats'],
  queryFn: () => api.get<AdminStats>('/api/admin/stats'),
  retry: false,
})

// Served on the admin host (admin.kutaapp.com) only; the API answers 404 to accounts that are not admins.
export function AdminPage() {
  const { t, i18n } = useTranslation()
  const stats = useQuery(adminStatsQuery)
  const notAdmin = stats.error instanceof ApiError && stats.error.status === 404
  const number = new Intl.NumberFormat(i18n.resolvedLanguage)
  return (
    <>
      <Heading level={1}>{t('admin.title')}</Heading>
      <SlowLoading loading={stats.isPending}>{null}</SlowLoading>
      {notAdmin ? (
        <p className="mt-4 text-body">{t('admin.notAdmin')}</p>
      ) : (
        <QueryError query={stats} className="mt-4" />
      )}
      {stats.data && (
        <dl className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-3">
          {(
            [
              ['accounts', stats.data.accounts],
              ['confirmedAccounts', stats.data.confirmedAccounts],
              ['newAccounts30Days', stats.data.newAccounts30Days],
              ['signedIn30Days', stats.data.signedIn30Days],
              ['accountsWithActivities', stats.data.accountsWithActivities],
              ['activities', stats.data.activities],
              ['newActivities30Days', stats.data.newActivities30Days],
            ] as const
          ).map(([key, value]) => (
            <Stat key={key} boxed label={t(`admin.${key}`)} value={number.format(value)} />
          ))}
        </dl>
      )}
    </>
  )
}
