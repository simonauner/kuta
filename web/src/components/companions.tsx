import { useQuery } from '@tanstack/react-query'
import { Link, useRouterState } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Avatar } from '@/components/avatar'
import { HelpPopover } from '@/components/help-popover'
import { api } from '@/lib/api'

type Companion = {
  activityId: string
  userId: string
  displayName: string
  avatarId: string | null
}

// Others who were out at the same time and place; only those whose activity the viewer may read.
export function Companions({ activityId, own }: { activityId: string; own: boolean }) {
  const { t } = useTranslation()
  const returnTo = useRouterState({ select: (s) => s.location.href })
  const companions = useQuery({
    queryKey: ['activities', activityId, 'companions'],
    queryFn: () => api.get<Companion[]>(`/api/activities/${activityId}/companions`),
  })
  if (!companions.data?.length) return null
  return (
    <section aria-labelledby="companions-title" className="mt-5">
      <div className="flex items-center gap-1">
        <p id="companions-title" className="text-metadata text-muted-foreground">
          {t('companions.title')}
        </p>
        <HelpPopover label={t('companions.helpLabel')} small>
          {t('companions.help')}
          {own && ` ${t('companions.ownHelp')}`}
        </HelpPopover>
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-2">
        {companions.data.map((companion) => (
          <li key={companion.activityId} className="flex items-center gap-2">
            <Avatar name={companion.displayName} avatarId={companion.avatarId} size="sm" />
            <Link
              to="/app/activities/$activityId"
              params={{ activityId: companion.activityId }}
              search={{ returnTo }}
              className="text-sm underline underline-offset-4"
            >
              {companion.displayName}
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
