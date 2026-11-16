import { useQuery } from '@tanstack/react-query'
import { getRouteApi, Link } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { AchievementItem, timeZone } from '@/components/achievements'
import { GarminDerivedNote } from '@/components/garmin'
import { QueryError } from '@/components/query-error'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Card } from '@/components/ui/card'
import { FormField } from '@/components/ui/form-field'
import { Heading } from '@/components/ui/heading'
import { Select } from '@/components/ui/select'
import { achievementsQuery } from '@/lib/achievements'

const route = getRouteApi('/app/profile/achievements')

// All own achievements, newest first, of all time or within one year (local time, as the year review).
export function AchievementsPage() {
  const { t } = useTranslation()
  const { year } = route.useSearch()
  const navigate = route.useNavigate()
  const achievements = useQuery(achievementsQuery(timeZone()))
  const yearOf = (achievedAt: string) => new Date(achievedAt).getFullYear()
  const years = [...new Set(achievements.data?.map((a) => yearOf(a.achievedAt)))]
  const shown = achievements.data?.filter(
    (a) => year === undefined || yearOf(a.achievedAt) === year,
  )

  return (
    <section>
      <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/profile" />}>{t('nav.profile')}</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('achievements.title')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <Heading level={1}>{t('achievements.title')}</Heading>
      <p className="mt-1 text-body text-muted-foreground">{t('achievements.byYearDescription')}</p>
      <GarminDerivedNote className="mt-1" />
      <FormField layout="inline" label={t('activities.year')} className="mt-4 text-sm">
        {(id) => (
          <Select
            id={id}
            className="w-32"
            value={year ?? ''}
            onChange={(e) =>
              void navigate({ search: e.target.value ? { year: Number(e.target.value) } : {} })
            }
          >
            <option value="">{t('activities.allYears')}</option>
            {year !== undefined && !years.includes(year) && <option value={year}>{year}</option>}
            {years.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </Select>
        )}
      </FormField>
      <QueryError query={achievements} className="mt-4" />
      {shown &&
        (shown.length === 0 ? (
          <p className="mt-4 text-body text-muted-foreground">{t('achievements.emptyYear')}</p>
        ) : (
          <Card className="mt-4">
            <ul className="space-y-4">
              {shown.map((a) => (
                <AchievementItem key={a.key} achievement={a} />
              ))}
            </ul>
          </Card>
        ))}
    </section>
  )
}
