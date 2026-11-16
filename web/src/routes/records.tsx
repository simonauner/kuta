import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { getRouteApi, Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { EffortLink } from '@/components/records'
import { QueryError } from '@/components/query-error'
import { SlowRefresh } from '@/components/slow-loading'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { Select } from '@/components/ui/select'
import { api } from '@/lib/api'
import { formatPace } from '@/lib/activity'
import { activityYearsQuery } from '@/lib/activity-query'
import { recordDistances, type RecordDistance, type StoredBestEffort } from '@/lib/records'

export function recordsSearch(search: Record<string, unknown>): { year?: number } {
  const year = Number(search.year)
  return Number.isInteger(year) && year >= 1 && year <= 9998 ? { year } : {}
}

const route = getRouteApi('/app/stats/records')

// Best effort per distance, of all time or within one UTC year.
export function RecordsPage() {
  const { t, i18n } = useTranslation()
  const { year } = route.useSearch()
  const navigate = route.useNavigate()
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  const years = useQuery(activityYearsQuery(Intl.DateTimeFormat().resolvedOptions().timeZone))
  const records = useQuery({
    queryKey: year === undefined ? ['records'] : ['records', 'year', year],
    queryFn: () =>
      api.get<StoredBestEffort[]>(
        year === undefined ? '/api/records' : `/api/records?year=${year}`,
      ),
    placeholderData: keepPreviousData,
  })

  return (
    <section>
      <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/stats" activeOptions={{ exact: true }} />}>
              {t('nav.stats')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('records.title')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <Heading level={1}>{t('records.title')}</Heading>
      <p className="mt-1 text-body text-muted-foreground">{t('records.byYearDescription')}</p>
      <FormField layout="inline" label={t('activities.year')} className="mt-4 text-sm">
        {(id) => (
          <Select
            id={id}
            className="w-32"
            value={year ?? ''}
            onChange={(e) =>
              void navigate({
                search: e.target.value ? { year: Number(e.target.value) } : {},
              })
            }
          >
            <option value="">{t('activities.allYears')}</option>
            {year !== undefined && !years.data?.includes(year) && (
              <option value={year}>{year}</option>
            )}
            {years.data?.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </Select>
        )}
      </FormField>
      <QueryError query={records} className="mt-4" />
      {records.isSuccess && (
        <SlowRefresh loading={records.isPlaceholderData}>
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-right text-sm tabular-nums">
              <thead className="text-xs text-muted-foreground">
                <tr>
                  <th scope="col" className="py-3 pr-3 text-left">
                    {t('records.distance')}
                  </th>
                  <th scope="col" className="px-2">
                    {year === undefined
                      ? t('records.personalBest')
                      : t('records.yearBest', { year })}
                  </th>
                  <th scope="col" className="px-2">
                    {t('activity.pace')}
                  </th>
                  <th scope="col" className="pl-2">
                    {t('records.date')}
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {recordDistances.map(([d, meters]) => {
                  const effort = records.data.find((e) => e.distanceM === d)
                  return (
                    <tr key={d}>
                      <th scope="row" className="py-3 pr-3 text-left font-medium">
                        {t(`records.distances.${d as RecordDistance}`)}
                      </th>
                      <td className="px-2">{effort ? <EffortLink effort={effort} /> : '–'}</td>
                      <td className="px-2">{effort ? formatPace(effort.elapsedS, meters) : '–'}</td>
                      <td className="pl-2 text-muted-foreground">
                        {effort ? date.format(new Date(effort.achievedAt)) : '–'}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </SlowRefresh>
      )}
    </section>
  )
}
